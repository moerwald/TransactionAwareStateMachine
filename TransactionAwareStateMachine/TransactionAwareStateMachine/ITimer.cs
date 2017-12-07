using System.Timers;

namespace TransactionAwareStateMachine
{
    public interface ITimer
    {
        event ElapsedEventHandler Elapsed;
        bool Enabled { get; set; }
        bool AutoReset { get; set; }
        double Interval { get; set; }

        void Start();
        void Stop();
    }

   
}
