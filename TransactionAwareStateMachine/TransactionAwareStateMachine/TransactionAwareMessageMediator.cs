using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace TransactionAwareStateMachine
{
    class TransactionAwareMessageMediator<TInMessage>
    {
        /// <summary>
        /// The target block to send messages from the statemachine to.
        /// </summary>
        private ITargetBlock<IImmutableList<RoutingRequest_V2<TInMessage>>> _target;

        /// <summary>
        /// The state machine that handles incoming messages. The state machine creates a list of outgoing messages that shall be sent to <see cref="_target"/>
        /// </summary>
        private IStateMachine_V2<TInMessage> _stateMachine;

        /// <summary>
        /// Buffer for input messages.
        /// </summary>
        private BufferBlock<TInMessage> _inputBlock;

        /// <summary>
        /// Puts incoming messages to the internal queue.
        /// </summary>
        private TransformBlock<TInMessage, IEnumerable<RoutingRequest_V2<TInMessage>>> _computationBlock;
        private CancellationTokenSource _tcs;
        private Task _checkInputQTask;

        /// <summary>
        /// Buffers incoming messages if the statemachine has an ongoing pending transaction.
        /// </summary>
        private readonly ConcurrentQueue<TInMessage> _inMessageQ = new ConcurrentQueue<TInMessage>();
        private readonly object _stateMachineLock = new object();

        public TransactionAwareMessageMediator
           (ITargetBlock<IImmutableList<RoutingRequest_V2<TInMessage>>> target
           , IStateMachine_V2<TInMessage> stateMachine
           , ITimerFactory timerFactory
           , TimeSpan deleteMessagesOlderThan)
        {
            // Param check
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

            // Create and link dataflow blocks
            _inputBlock = new BufferBlock<TInMessage>(new ExecutionDataflowBlockOptions { CancellationToken = _tcs.Token });
            _computationBlock = new TransformBlock<TInMessage, IEnumerable<RoutingRequest_V2<TInMessage>>>
                (inMessage => HandleInputMessage(inMessage)
                , new ExecutionDataflowBlockOptions { BoundedCapacity = 1, CancellationToken = _tcs.Token });

            _inputBlock.LinkTo(_computationBlock, new DataflowLinkOptions { PropagateCompletion = true });
            _tcs = new CancellationTokenSource();

            _checkInputQTask = CheckInputMessageQ();
        }

        /// <summary>
        /// We don't want to return a null reference as IEnumerable. Therefore we create an immutable collection, to signal failures.
        /// </summary>
        private readonly IReadOnlyCollection<RoutingRequest_V2<TInMessage>> _emptyList = new List<RoutingRequest_V2<TInMessage>>();

        private IEnumerable<RoutingRequest_V2<TInMessage>> HandleInputMessage(TInMessage message)
        {
            if (_inMessageQ.IsEmpty && _stateMachine.HasPendingTransaction == false)
            {
                // We can instantly send the message to the state machine
                lock (_stateMachineLock)
                {
                    _stateMachine.InsertMessage(message);
                }

                if (_stateMachine.Result.Count > 1)
                {
                    // Result will be forwarded by TPL to the target block.
                    return _stateMachine.Result;
                }
            }
            else
            {
                // Some transaction is ongoing -> Q the message
                _inMessageQ.Enqueue(message);
            }

            return _emptyList;
        }

        private Task CheckInputMessageQ()
        {
            return Task.Factory.StartNew(async () => 
            {
                while (_tcs.Token.IsCancellationRequested == false)
                {
                    try
                    {
                        if (_inMessageQ.IsEmpty)
                        {
                            // Nothing to do
                            await Task.Delay(50);
                        }
                        else
                        {
                            // Some messages in Q
                            if (_stateMachine.HasPendingTransaction)
                            {
                                // We have a pending transaction and at least one item in the Q
                                if (_stateMachine.PendingTransactionTimeoutOccurred)
                                {
                                    _stateMachine.RollbackPendingTransaction();
                                    await DequeueMessage_CallStateMachine_AndSendResultTowardsRouter();
                                }
                            }
                            else
                            {
                                await DequeueMessage_CallStateMachine_AndSendResultTowardsRouter();
                            }
                        }
                    }
                    catch(TaskCanceledException)
                    {
                        // Task was shutdown
                    }
                }

                #region local helper
                async Task DequeueMessage_CallStateMachine_AndSendResultTowardsRouter()
                {
                    if (_inMessageQ.TryDequeue(out TInMessage message))
                    {
                        lock (_stateMachineLock)
                        {
                            _stateMachine.InsertMessage(message);
                        }

                        if (await _target.SendAsync(_stateMachine.Result, _tcs.Token) == false)
                        {
                            // Logging
                        }
                    }
                }
                #endregion
            }, _tcs.Token);
        }

        public async Task<bool> PostMessageAsync(TInMessage inMessage) => await _inputBlock.SendAsync(inMessage);

        public bool PostMessage(TInMessage inMessage) => _inputBlock.Post(inMessage);

        public void Shutdown()
        {
            _tcs.Cancel();
            Task.WaitAll(new[] { _checkInputQTask, _inputBlock.Completion });
        }

        public async Task ShutdownAsync()
        {
            _tcs.Cancel();
            await _checkInputQTask;
            await _inputBlock.Completion;
        }
    }
}
