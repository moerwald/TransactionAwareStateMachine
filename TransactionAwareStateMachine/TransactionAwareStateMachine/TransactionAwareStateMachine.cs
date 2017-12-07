using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Collections.Concurrent;
using System.Threading;
using System.Timers;
using System;
using System.Collections.Immutable;

namespace TransactionAwareStateMachine
{

    public class TransactionAwareStateMachine<TInMessage, TDestination>
    {
        public TransactionAwareStateMachine
            ( ITargetBlock<ImmutableList<RoutingRequest<TInMessage, TDestination>>> routerTarget
            , IStateMachine<TInMessage, TDestination> stateMachine
            , ITimerFactory timerFactory
            , TimeSpan deleteMessagesOlderThan)
        {
            // Param check
            _routerTarget = routerTarget ?? throw new ArgumentNullException(nameof(routerTarget));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
            this.deleteMessagesOlderThan = deleteMessagesOlderThan;


            // Create and link dataflow blocks
            _inputBlock = new BufferBlock<TInMessage>(new ExecutionDataflowBlockOptions { CancellationToken = _tcs.Token });
            _computationBlock = new TransformBlock<TInMessage, IEnumerable<RoutingRequest<TInMessage, TDestination>>>
                (inMessage => HandleInputMessage(inMessage)
                , new ExecutionDataflowBlockOptions { BoundedCapacity = 1, CancellationToken = _tcs.Token });

            _inputBlock.LinkTo(_computationBlock, new DataflowLinkOptions { PropagateCompletion = true });
            _tcs = new CancellationTokenSource();

            _timer = _timerFactory.Create();
            _timer.Interval = deleteMessagesOlderThan.Milliseconds;
            _timer.Enabled = true;
            _timer.Elapsed += (sender, args) => TimeoutOccurred(sender, args);
        }

        public void Shutdown()
        {
            _timer.Stop();
            _tcs.Cancel();
        }

        public ITargetBlock<ImmutableList<RoutingRequest<TInMessage, TDestination>>> _routerTarget;
        public IStateMachine<TInMessage, TDestination> _stateMachine;
        private readonly ITimerFactory _timerFactory;
        private TimeSpan deleteMessagesOlderThan;
        private readonly TimeSpan _deleteMessagesOlderThan;
        private readonly CancellationTokenSource _tcs;
        private readonly ITimer _timer;
        private readonly BufferBlock<TInMessage> _inputBlock;
        private readonly TransformBlock<TInMessage, IEnumerable<RoutingRequest<TInMessage, TDestination>>> _computationBlock;
        private readonly ConcurrentQueue<TInMessage> _inMessageQ = new ConcurrentQueue<TInMessage>();
        private readonly object _stateMachineLock = new object();

        /// <summary>
        /// We don't want to return a null reference as IEnumerable. Therefore we create an immutable collection, to signal failures.
        /// </summary>
        private readonly IReadOnlyCollection<RoutingRequest<TInMessage, TDestination>> _emptyList = new List<RoutingRequest<TInMessage, TDestination>>();

        private async Task TimeoutOccurred(object sender, ElapsedEventArgs args)
        {
            if (_stateMachine.HasPendingTransaction)
            {
                // No answer was received in pending state -> tell the statemachine that transaction failed -> and feed it with the next message
                // in the Q
                _stateMachine.TransactionFailed();

                if (_inMessageQ.TryDequeue(out TInMessage message) == false)
                {
                    // Logging
                    return;
                }

                lock (_stateMachineLock)
                {
                    _stateMachine.InsertMessage(message);
                }

                //var sendSucceeded = await _routerTarget.SendAsync(_stateMachine.Result);
                //if (sendSucceeded == false)
                //{
                //    // Logging
                //}
            }
        }

        private IEnumerable<RoutingRequest<TInMessage, TDestination>> HandleInputMessage(TInMessage message)
        {
            if (_stateMachine.HasPendingTransaction)
            {
                // We've a pending transaction -> don't insert next message to state machine -> store it in the internal Q.
                _inMessageQ.Enqueue(message);
                return _emptyList;
            }
            else
            {
                // No pending transaction -> we feed the state machine!
                var msg = message;
                if (_inMessageQ.Count > 1)
                {
                    // Store the actual message and get the oldest one
                    _inMessageQ.Enqueue(message);   
                    if (_inMessageQ.TryDequeue(out msg) == false)   
                    {
                        // Logging
                        return _emptyList;
                    }
                }

                lock (_stateMachineLock)
                {
                    _stateMachine.InsertMessage(msg);
                }

                if (_stateMachine.Result.Count > 1)
                {
                    _timer.Start();
                    return _stateMachine.Result;
                }

                return _emptyList;
            }
        }

        public async Task<bool> PostMessageAsync(TInMessage inMessage) => await _inputBlock.SendAsync(inMessage);

        public bool PostMessage(TInMessage inMessage) => _inputBlock.Post(inMessage);
    }
}
