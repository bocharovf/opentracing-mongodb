using MongoDB.Driver.Core.Events;
using System;

namespace OpenTracing.Contrib.MongoDB
{
    public class InstrumentationOptions
    {
        public ITracer Tracer { get; set; }
        public Action<string> ProcessCommandText { get; set; }
        public bool LogCommandTextToSpan { get; set; }
        public Func<CommandStartedEvent, bool> ShouldStartSpan { get; set; }
    }
}