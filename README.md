# opentracing-mongodb

OpenTracing instrumentation for MongoDB Driver 2.3 to 3.0. 
Based on [MongoDB.Driver.Core.Extensions.DiagnosticSources](https://github.com/jbogard/MongoDB.Driver.Core.Extensions.DiagnosticSources) - consider to use it and newer [OpenTelemetry](https://github.com/open-telemetry/opentelemetry-dotnet) if applicable.

## Usage

Configure your `MongoClientSettings` to add MongoDB event subscriber:

```csharp
var clientSettings = MongoClientSettings.FromUrl(mongoUrl);
clientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber());
var mongoClient = new MongoClient(clientSettings);
```

To capture the command text as part of the activity use ```ProcessCommandText``` callback:

```csharp
var clientSettings = MongoClientSettings.FromUrl(mongoUrl);
var options = new InstrumentationOptions { ProcessCommandText = text => Console.WriteLine(text) };
clientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(options));
var mongoClient = new MongoClient(clientSettings);
```

To filter activities by collection name:

```csharp
var clientSettings = MongoClientSettings.FromUrl(mongoUrl);
var options = new InstrumentationOptions { ShouldStartSpan = @event => !"collectionToIgnore".Equals(@event.GetCollectionName()) };
clientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(options));
var mongoClient = new MongoClient(clientSettings);
```

It uses ```OpenTracing.Util.GlobalTracer.Instance``` by default but you could pass specific Tracer:

```csharp
var clientSettings = MongoClientSettings.FromUrl(mongoUrl);
var options = new InstrumentationOptions { Tracer = tracer };
clientSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(options));
var mongoClient = new MongoClient(clientSettings);
```
