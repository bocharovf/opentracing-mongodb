using MongoDB.Driver.Core.Events;
using OpenTracing;
using OpenTracing.Tag;
using OpenTracing.Util;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace OpenTracing.Contrib.MongoDB
{
    public class DiagnosticsActivityEventSubscriber : IEventSubscriber
    {
        private readonly InstrumentationOptions _options;
        public const string ActivityName = "MongoDB.Driver.Core.Events.Command";

        private readonly ReflectionEventSubscriber _subscriber;
        private readonly ConcurrentDictionary<int, ISpan> _activityMap = new();

        public DiagnosticsActivityEventSubscriber() : this(new InstrumentationOptions { ProcessCommandText = null })
        {
        }

        public DiagnosticsActivityEventSubscriber(InstrumentationOptions options)
        {
            _options = options;
            _subscriber = new ReflectionEventSubscriber(this, bindingFlags: BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public bool TryGetEventHandler<TEvent>(out Action<TEvent> handler)
            => _subscriber.TryGetEventHandler(out handler);

        private void Handle(CommandStartedEvent @event)
        {
            var tracer = _options.Tracer ?? GlobalTracer.Instance;

            if (_options.ShouldStartSpan != null && !_options.ShouldStartSpan(@event) || tracer == null)
            {
                return;
            }

            var collectionName = @event.GetCollectionName();

            // https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/semantic_conventions/database.md
            var operationName = collectionName == null ? $"mongodb.{@event.CommandName}" : $"{collectionName}.{@event.CommandName}";

            var span = tracer.BuildSpan(operationName)
                .WithTag(Tags.SpanKind, Tags.SpanKindClient)
                .Start();

            span.SetTag("db.system", "mongodb");
            span.SetTag("db.name", @event.DatabaseNamespace?.DatabaseName);
            span.SetTag("db.mongodb.collection", collectionName);
            span.SetTag("db.operation", @event.CommandName);
            var endPoint = @event.ConnectionId?.ServerId?.EndPoint;
            switch (endPoint)
            {
                case IPEndPoint ipEndPoint:
                    span.SetTag("db.user", $"mongodb://{ipEndPoint.Address}:{ipEndPoint.Port}");
                    span.SetTag("net.peer.ip", ipEndPoint.Address.ToString());
                    span.SetTag("net.peer.port", ipEndPoint.Port.ToString());
                    break;
                case DnsEndPoint dnsEndPoint:
                    span.SetTag("db.user", $"mongodb://{dnsEndPoint.Host}:{dnsEndPoint.Port}");
                    span.SetTag("net.peer.name", dnsEndPoint.Host);
                    span.SetTag("net.peer.port", dnsEndPoint.Port.ToString());
                    break;
            }

            if (_options.LogCommandTextToSpan)
            {
	            span.Log(@event.Command.ToString());
            }

            if (_options.ProcessCommandText != null)
            {
                _options.ProcessCommandText(@event.Command.ToString());
            }

            _activityMap.TryAdd(@event.RequestId, span);
        }

        private void Handle(CommandSucceededEvent @event)
        {
            if (_activityMap.TryRemove(@event.RequestId, out var span))
            {
                span.SetTag("otel.status_code", "Ok");
                span.Finish();
            }
        }

        private void Handle(CommandFailedEvent @event)
        {
            if (_activityMap.TryRemove(@event.RequestId, out var activity))
            {
                activity.SetTag("otel.status_code", "Error");
                activity.SetTag("otel.status_description", @event.Failure.Message);
                activity.SetTag("error.type", @event.Failure.GetType().FullName);
                activity.SetTag("error.msg", @event.Failure.Message);
                activity.SetTag("error.stack", @event.Failure.StackTrace);
                activity.SetTag(Tags.Error, true);
                activity.Finish();
            }
        }
    }
}
