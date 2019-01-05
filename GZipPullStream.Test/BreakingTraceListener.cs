using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GZipPullStream.Test
{
    internal class BreakingTraceListener
    {
        static BreakingTraceListener()
        {
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new Listener());
        }

        public static void Setup()
        {
            // Nothing to do.
        }

        private class Listener : TraceListener
        {
            public override void Write(string message)
            {
                Console.Write(message);
            }

            public override void WriteLine(string message)
            {
                Console.WriteLine(message);
            }

            [DebuggerHidden]
            public override void Fail(string message, string detailMessage)
            {
                base.Fail(message, detailMessage);
                Debugger.Break();
            }
        }
    }
}
