using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;


var builder = NanoApplication.CreateBuilder();
builder.services.AddSingleton<IGreetingService, GreetingService>();
builder.services.AddSingleton<IMessageService, MessageService>();

var app = builder.Build();

app.MapGet("/hello", async (HttpContext ctx) =>
{
    ctx.Response.ResponseBody = "Hello World!";
    ctx.Response.Header = "Content-Type: text/plain";
});

app.MapGet("/hello1", (IGreetingService svc, HttpContext ctx) => svc.Greet(ctx));

app.MapGet("/hello/{name}", (HttpContext ctx) => {
    ctx.Response.ResponseBody = $"Hello {ctx.Request.RouteValues["name"]}!";
});

app.MapGet("/hello1/{name}", (string name, HttpContext ctx) => {
    ctx.Response.ResponseBody = $"Hello {name}!";
});


//Takes ctx and next, returns a Task.
app.Use(async (ctx, next) =>
{
    Console.WriteLine("this is nano");
    await next(ctx);
});

app.UseRouting();

await app.Run("http://localhost:5050");


public interface IMessageService
{
    string GetMessage();
}

public class MessageService : IMessageService
{
    public string GetMessage() => "Hello from MessageService!";
}

public interface IGreetingService
{
    public void Greet(HttpContext ctx);
}

public class GreetingService : IGreetingService
{
    public IMessageService _messageService { get; set; }
    public GreetingService(IMessageService service)
    {
        this._messageService = service;
    }
    public void Greet(HttpContext ctx)
    {
        Console.WriteLine(_messageService.GetMessage());
        ctx.Response.ResponseBody = _messageService.GetMessage() + "Greetings";
        ctx.Response.Header = "Content-Type: text/plain";
    }
}

public class ServiceDescriptor
{
    public Type ServiceType { get; set; }
    public Type ImplementationType { get; set; }
    public Lifetime Lifetime { get; set; }
    public object? Instance { get; set; }
}

public enum Lifetime
{
    Scoped,
    Singleton,
    Transient
}

public class ServiceCollection
{
    public List<ServiceDescriptor> serviceDescriptors { get; set; } = new List<ServiceDescriptor>();
    public void AddSingleton<TService, TImplementation>()
    {
        var descriptor = new ServiceDescriptor() { ServiceType = typeof(TService), ImplementationType = typeof(TImplementation), Lifetime = Lifetime.Singleton };

        serviceDescriptors.Add(descriptor);
    }
}

public class NanoApplicationBuilder
{
    public ServiceCollection services { get; set; } = new ServiceCollection();

    public NanoApplication Build()
    {
        //probably DI engine stuff
        NanoApplication app = new NanoApplication() { _services = services };

        return app;
    }
}


public class NanoApplication
{
    public static NanoApplicationBuilder CreateBuilder()
    {
        var appBuilder = new NanoApplicationBuilder();

        return appBuilder;
    }
    public ServiceCollection _services { get; set; } = new ServiceCollection();

    public List<Func<RequestDelegate, RequestDelegate>> _middlewares = new List<Func<RequestDelegate, RequestDelegate>>();

    public RequestDelegate app;

    public TcpListener listener;

    public List<(string method, string pattern, Delegate handler)> RouteTable = new List<(string, string, Delegate)> { };

    public object GetService<TService>()
    {
        return GetService(typeof(TService)); ;
    }

    // We dont know the type in compiletime, we recursively take constructor parameters and build at runtime.
    public object GetService(Type serviceType)
    {
        var descriptor = _services.serviceDescriptors
        .FirstOrDefault(x => x.ServiceType == serviceType);

        if (descriptor == null)
            throw new InvalidOperationException($"Service of type {serviceType} not registered.");

        if (descriptor.Instance == null)
        {
            ConstructorInfo constructor = descriptor.ImplementationType.GetConstructors()[0];
            ParameterInfo[] parameters = constructor.GetParameters();

            var resolvedParams = parameters.Select(p => GetService(p.ParameterType)).ToArray();
            descriptor.Instance = Activator.CreateInstance(descriptor.ImplementationType, resolvedParams);
        }
        return descriptor.Instance;
    }

    public void MapGet(string route, Delegate handler)
    {
        RouteTable.Add(("GET", route, handler));
    }

    public void UseRouting()
    {
        this.Use(RouteHandler);
    }
    public async Task RouteHandler(HttpContext context, RequestDelegate next)
    {

        bool matched = false;
        int matchedIndex = 0;

        for (int i = 0;  i < this.RouteTable.Count; i++)
        {
            var registeredRoute = this.RouteTable[i];
            if (registeredRoute.method != context.Request.Method) continue;
            if (MatchRoute(registeredRoute.pattern, context.Request.Route))
            {
                matched = true;
                matchedIndex = i;
            }
        }
        try
        {
            if (matched)
            {
                context.Request.RouteValues = ExtractRouteValues(this.RouteTable[matchedIndex].pattern, context.Request.Route);
                await this.InvokeHandler(this.RouteTable[matchedIndex].handler, context);
                Console.WriteLine("invoke done");
            }
            else
            {
                context.Response.StatusCode = HttpStatusCode.NotFound;
                context.Response.ResponseBody = "Not Found";

                await next(context);
                return;
            }
        }
        catch (Exception)
        {
            context.Response.StatusCode = HttpStatusCode.InternalServerError;
            context.Response.ResponseBody = "Internal Server Error";
            return;
        }
    }

    public bool MatchRoute(string pattern, string incoming)
    {
        string[] regSegments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] incSegments = incoming.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (regSegments.Length != incSegments.Length) return false;

        for (int i = 0;  i < regSegments.Length; i++)
        {
            if (regSegments[i].StartsWith("{")) continue;

            if (!regSegments[i].Equals(incSegments[i], StringComparison.Ordinal)) return false;
        }

        return true;
    }

    public Dictionary<string, string> ExtractRouteValues(string pattern, string incoming)
    {
        string[] regSegments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] incSegments = incoming.Split('/', StringSplitOptions.RemoveEmptyEntries);

        Dictionary<string, string> routeValues = new Dictionary<string, string>();

        for (int i = 0; i < regSegments.Length; i++)
        {
            if (regSegments[i].StartsWith("{"))
            {
                routeValues.Add(regSegments[i].Substring(1, regSegments[i].Length - 2), incSegments[i]);
            }
        }

        return routeValues;
    }

    public async Task InvokeHandler(Delegate handler, HttpContext ctx)
    {
        var toResolve = handler.Method.GetParameters().ToList();

        var resolved = new List<object>();
        foreach (var parameterInfo in toResolve)
        {
            if (parameterInfo.ParameterType == typeof(HttpContext))
            {
                resolved.Add(ctx);
            }
            else
            {
                var routeValue = ctx.Request.RouteValues != null ? ctx.Request.RouteValues[parameterInfo.Name] : null;
                if (routeValue != null)
                    resolved.Add(routeValue);
                else
                    resolved.Add(GetService(parameterInfo.ParameterType));
            }
        }

        var result = handler.DynamicInvoke(resolved.ToArray());

        if (result is Task task)
            await task;

        return;
    }

    //Takes ctx and next, returns a Task. = Func<HttpContext, RequestDelegate, Task> = middleware(ctx, next)
    public void Use(Func<HttpContext, RequestDelegate, Task> middleware)
    {
        //Func<(next) = RequestDelegate , Func<(ctx) takes HttpContext, returns Task> = RequestDelegate >
        Func<RequestDelegate, RequestDelegate> factory = (next) => (ctx) => middleware(ctx, next);

        // Func<RequestDelegate, RequestDelegate> factory = 
        // function(next)
        // {
        //     return function(ctx)
        //     {
        //         return middleware(ctx, next);
        //     };
        // };

        _middlewares.Add(factory);
    }


    public void ComposeMiddleware()
    {
        RequestDelegate app = ctx => Task.CompletedTask;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            app = _middlewares[i](app);
        }
        this.app = app;
    }

    public async Task Run(string url)
    {
        Uri uri = new Uri(url);
        var ip = Dns.GetHostAddresses(uri.Host);
        TcpListener listener = new TcpListener(ip[0], uri.Port);
        listener.Start(); //socket() + bind() + listen().

        this.ComposeMiddleware();

        while (true)
        {
            Console.WriteLine("Loop Start");
            //accept(), one connection socket per client.
            TcpClient client = await listener.AcceptTcpClientAsync();

            // while loop runs on one thread. When it hits _ = Task.Run(...), it hands the work off to a thread pool thread and immediately continues to the next line
            _ = Task.Run(() => HandleConnection(client));
            Console.WriteLine("Loop End");
        }
    }

    public async Task HandleConnection(TcpClient client)
    {
        try
        {
            Console.WriteLine("Run start");

            NetworkStream stream = client.GetStream();

            byte[] buffer = new byte[1024];

            int readable = 0;

            byte[] endings = Encoding.UTF8.GetBytes("\r\n\r\n");
            List<byte> buffers = new List<byte> { };

            while (true) {
                int readablePartial = await stream.ReadAsync(buffer, 0, 1024);

                readable += readablePartial;

                buffers.AddRange(buffer[..(readablePartial)]);

                if (buffers.Skip(readable - endings.Length).SequenceEqual(endings))
                {
                    break;
                }
            }

            if (readable == 0)
                return;

            byte[] requestline = new byte[900];
            int end = 0;

            for (int i = 0; i < readable - 1; i++)
            {
                if (buffers[i] == 13 && buffers[i + 1] == 10)
                {
                    buffers.GetRange(0, i).ToArray().CopyTo(requestline);
                    end = i;
                    break;
                }
            }

            string line = Encoding.ASCII.GetString(requestline, 0, end);

            string[] parts = line.Split(' ');

            string Method = parts[0];
            string Route = parts[1];
            string Version = parts[2];

            HttpContext context = new HttpContext()
            {
                Request = new DefaultHttpRequest()
                {
                    Method = Method,
                    Route = Route,
                    Version = Version
                },
                Response = new DefaultHttpResponse()
            };

            await app.Invoke(context);

            string contentLength =
                Encoding.UTF8.GetByteCount(context.Response.ResponseBody).ToString();

            string response =
                $"HTTP/1.1 {(int)context.Response.StatusCode} {context.Response.StatusCode}\r\n" +
                $"Content-Length: {contentLength}\r\n" +
                $"Connection: close\r\n" +
                $"\r\n" +
                $"{context.Response.ResponseBody}";

            byte[] resbuffer = Encoding.UTF8.GetBytes(response);

            await stream.WriteAsync(resbuffer);
            await stream.FlushAsync();

            Console.WriteLine("Run End");
        }
        finally
        {
            // Browser side. HTTP/1.1 keep-alive means the browser holds the connection open waiting for more responses.
            // The browser sends a request and waits for the response.The browser sends a request and waits for the response. Code sends the response but the TCP connection stays open. The browser thinks more data might be coming, so it keeps waiting.
            client.Close(); //signals the end of the connection. The browser sees it, accepts the response, and moves on.
        }
    }
}


public delegate Task RequestDelegate(HttpContext context);


public class HttpContext
{
    public IHttpRequestFeature Request { get; set; }
    public IHttpResponseFeature Response { get; set; }
}

public class DefaultHttpRequest : IHttpRequestFeature
{
    public string Method { get; set; }
    public string Route { get; set; }
    public string Version { get; set; }

    public Dictionary<string, string> RouteValues { get; set; }
}
public class DefaultHttpResponse : IHttpResponseFeature
{
    public string ResponseBody { get; set; } = string.Empty;
    public string Header { get; set; }
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
}
public interface IHttpRequestFeature
{
    public string Method { get; set; }
    public string Route { get; set; }
    public string Version { get; set; }

    public Dictionary<string, string> RouteValues {  get; set; }
}

public interface IHttpResponseFeature
{
    public string ResponseBody { get; set; }
    public string Header { get; set; }
    public HttpStatusCode StatusCode { get; set; }
}