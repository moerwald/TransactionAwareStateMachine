using System.Collections.Immutable;

namespace TransactionAwareStateMachine
{
    public interface IStateMachine<TPayload, TDestination>
    {
        void InsertMessage(TPayload message);
        IImmutableList<RoutingRequest<TPayload, TDestination>> Result { get; }
        bool PendingTransactionTimeoutOccurred { get; }

        bool HasPendingTransaction { get; }

        void TransactionFailed();
        void RollbackPendingTransaction();
    }


    public interface IStateMachine_V2<TPayload>
    {
        void InsertMessage(TPayload message);
        IImmutableList<RoutingRequest_V2<TPayload>> Result { get; }
        bool PendingTransactionTimeoutOccurred { get; }

        bool HasPendingTransaction { get; }

        void TransactionFailed();
        void RollbackPendingTransaction();
    }


    public abstract class StateMachine_V2<TPayload> : IStateMachine_V2<TPayload>
    {
        public abstract IImmutableList<RoutingRequest_V2<TPayload>> Result { get; }
        public abstract bool PendingTransactionTimeoutOccurred { get; }
        public abstract bool HasPendingTransaction { get; }

        public abstract void InsertMessage(TPayload message);
        public abstract void RollbackPendingTransaction();
        public abstract void TransactionFailed();
    }


}
