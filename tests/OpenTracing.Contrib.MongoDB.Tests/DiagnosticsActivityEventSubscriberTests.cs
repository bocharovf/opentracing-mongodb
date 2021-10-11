using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using OpenTracing.Mock;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Xunit;

namespace OpenTracing.Contrib.MongoDB.Tests
{
    public class DiagnosticsActivityEventSubscriberTests
    {
        static DiagnosticsActivityEventSubscriberTests()
        {
        }
        
        [Fact]
        public void Should_start_and_log_successful_activity()
        {
            var tracer = new MockTracer();
            var behavior = new DiagnosticsActivityEventSubscriber(new InstrumentationOptions() { Tracer = tracer });

            behavior.TryGetEventHandler<CommandStartedEvent>(out var startEvent).ShouldBeTrue();
            behavior.TryGetEventHandler<CommandSucceededEvent>(out var stopEvent).ShouldBeTrue();

            startEvent(new CommandStartedEvent());
            stopEvent(new CommandSucceededEvent());

            var spans = tracer.FinishedSpans();
            spans.Count.ShouldBe(1);
            spans.First().Tags.ShouldContainKey("db.name");
            spans.First().Tags.ShouldContainKey("otel.status_code");
        }

        [Fact]
        public void Should_start_and_log_failed_activity()
        {
            var tracer = new MockTracer();
            var behavior = new DiagnosticsActivityEventSubscriber(new InstrumentationOptions() { Tracer = tracer });

            behavior.TryGetEventHandler<CommandStartedEvent>(out var startEvent).ShouldBeTrue();
            behavior.TryGetEventHandler<CommandFailedEvent>(out var stopEvent).ShouldBeTrue();

            var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 8000)));
            var databaseNamespace = new DatabaseNamespace("test");
            var command = new BsonDocument(new Dictionary<string, object>
            {
                {"update", "my_collection"}
            });
            startEvent(new CommandStartedEvent("update", command, databaseNamespace, null, 1, connectionId));
            stopEvent(new CommandFailedEvent("update", new Exception("Failed"), null, 1, connectionId, TimeSpan.Zero));

            var spans = tracer.FinishedSpans();
            spans.Count.ShouldBe(1);
            spans.First().Tags.ShouldContainKey("error.type");
            spans.First().Tags.ShouldContainKey("error.msg");
            spans.First().Tags.ShouldContainKey("error.stack");
        }

        [Fact]
        public void Should_record_command_text_when_option_set()
        {
            string statement = null;
            
            var tracer = new MockTracer();
            var options = new InstrumentationOptions()
            {
                Tracer = tracer,
                ProcessCommandText = text => statement = text
            };

            var behavior = new DiagnosticsActivityEventSubscriber(options);

            var command = new BsonDocument(new Dictionary<string, object>
            {
                {"update", "my_collection"}
            });

            behavior.TryGetEventHandler<CommandStartedEvent>(out var startEvent).ShouldBeTrue();
            behavior.TryGetEventHandler<CommandSucceededEvent>(out var stopEvent).ShouldBeTrue();

            var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 8000)));
            var databaseNamespace = new DatabaseNamespace("test");
            startEvent(new CommandStartedEvent("update", command, databaseNamespace, null, 1, connectionId));
            stopEvent(new CommandSucceededEvent("update", command, null, 1, connectionId, TimeSpan.Zero));

            statement.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public void Should_handle_parallel_activities()
        {
            var tracer = new MockTracer();
            var behavior = new DiagnosticsActivityEventSubscriber(new InstrumentationOptions() { Tracer = tracer });

            behavior.TryGetEventHandler<CommandStartedEvent>(out var startEvent).ShouldBeTrue();
            behavior.TryGetEventHandler<CommandSucceededEvent>(out var stopEvent).ShouldBeTrue();

            var outerActivity = tracer.BuildSpan("Outer").StartActive();

            var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 8000)));
            var databaseNamespace = new DatabaseNamespace("test");
            var updateCommand = new BsonDocument(new Dictionary<string, object>
            {
                {"update", "my_collection"}
            });
            var insertCommand = new BsonDocument(new Dictionary<string, object>
            {
                {"insert", "my_collection"}
            });
            startEvent(new CommandStartedEvent("update", updateCommand, databaseNamespace, null, 1, connectionId));
            startEvent(new CommandStartedEvent("insert", insertCommand, databaseNamespace, null, 2, connectionId));
            stopEvent(new CommandSucceededEvent("update", updateCommand, null, 1, connectionId, TimeSpan.Zero));
            stopEvent(new CommandSucceededEvent("insert", insertCommand, null, 2, connectionId, TimeSpan.Zero));

            outerActivity.Span.Finish();

            var spans = tracer.FinishedSpans();
            var root = spans.First(s => s.ParentId == null);
            
            spans.Count.ShouldBe(3);
            spans.Count(s => s.Context.TraceId == root.Context.TraceId).ShouldBe(3);
            spans.Count(s => s.ParentId == root.Context.SpanId).ShouldBe(2);
        }

        [Fact]
        public void Should_filter_activities()
        {
            var tracer = new MockTracer();
            var options = new InstrumentationOptions()
            {
                Tracer = tracer,
                ShouldStartSpan = activity => activity.CommandName == "insert"
            };
            var behavior = new DiagnosticsActivityEventSubscriber(options);

            behavior.TryGetEventHandler<CommandStartedEvent>(out var startEvent).ShouldBeTrue();
            behavior.TryGetEventHandler<CommandSucceededEvent>(out var stopEvent).ShouldBeTrue();

            var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 8000)));
            var databaseNamespace = new DatabaseNamespace("test");
            var updateCommand = new BsonDocument(new Dictionary<string, object>
            {
                {"update", "my_collection"}
            });
            var insertCommand = new BsonDocument(new Dictionary<string, object>
            {
                {"insert", "my_collection"}
            });
            startEvent(new CommandStartedEvent("update", updateCommand, databaseNamespace, null, 1, connectionId));
            startEvent(new CommandStartedEvent("insert", insertCommand, databaseNamespace, null, 2, connectionId));
            stopEvent(new CommandSucceededEvent("update", updateCommand, null, 1, connectionId, TimeSpan.Zero));
            stopEvent(new CommandSucceededEvent("insert", insertCommand, null, 2, connectionId, TimeSpan.Zero));

            var spans = tracer.FinishedSpans();

            spans.Count.ShouldBe(1);
            spans.First(s => s.OperationName == "my_collection.insert");
        }
    }
}
