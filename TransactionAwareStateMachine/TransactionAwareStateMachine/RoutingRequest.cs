using System;

namespace TransactionAwareStateMachine
{
    public class RoutingRequest<TPayload, TDestination> 
    {
        public RoutingRequest(TPayload payload, TDestination destination, ITime time)
        {
            Payload = payload;
            Destination = destination;
            CreationTime = time.NowUtc();
        }

        public TPayload Payload { get; }
        public TDestination Destination { get; }

        public DateTime CreationTime { get; }
    }


    public class RoutingRequest_V2<TPayload>
    {
        public RoutingRequest_V2(TPayload payload, string destination)
        {
            Payload = payload;
            Destination = destination;
        }

        public TPayload Payload { get; }
        public string Destination { get; }

    }

}
