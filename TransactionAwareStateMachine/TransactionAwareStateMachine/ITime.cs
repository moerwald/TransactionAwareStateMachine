using System;

namespace TransactionAwareStateMachine
{
    public interface ITime
    {
        DateTime NowUtc();
    }

   
}
