using System;

namespace TransactionAwareStateMachine
{
    public class Time : ITime
    {
        public DateTime NowUtc() => DateTime.UtcNow;
    }

   
}
