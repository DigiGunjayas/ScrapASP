using System.Net;
using System.Net.Sockets;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;


Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
serverSocket.Bind(new IPEndPoint(IPAddress.Any, 5050));
serverSocket.Listen(128);

NanoApplication app = new NanoApplication() { serverSocket = serverSocket };

app.AddSingleton<IGreetingService, GreetingService>();

app.MapGet("/hello", async (HttpContext ctx) => {
ctx.Response.ResponseBody = "Hello World!";
ctx.Response.Header = "Content-Type: text/plain";
});

app.MapGet("/hello1", (IGreetingService svc, HttpContext ctx) => svc.Greet(ctx));

app.Use(async (ctx, next) => {
Console.WriteLine("this is scrapASP");
await next(ctx);
});

app.UseRouting();
app.Build();

await app.Run();


public interface IGreetingService
{
    public void Greet(HttpContext ctx);
}

public class GreetingService : IGreetingService
{
    public void Greet(HttpContext ctx)
    {
        Console.WriteLine("hello!!");
        ctx.Response.ResponseBody = "Hello!";
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

public enum Lifetime { Scoped, Singleton, Transient }

public class ServiceCollection
{
    public List<ServiceDescriptor> serviceDescriptors { get; set; } = new();
}


public class NanoApplication
{
    public ServiceCollection _services { get; set; } = new();
    public List<Func<RequestDelegate, RequestDelegate>> _middlewares = new();
    public RequestDelegate app;
    public Socket serverSocket;                      
    public Dictionary<string, Delegate> RouteTable = new();

    public void AddSingleton<TService, TImplementation>()
    {
        var descriptor = new ServiceDescriptor
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TImplementation),
            Lifetime = Lifetime.Singleton
        };
        _services.serviceDescriptors.Add(descriptor);
    }

    public object GetService<TService>() => GetService(typeof(TService));
    public object GetService(Type serviceType)
    {
        var descriptor = _services.serviceDescriptors
            .FirstOrDefault(x => x.ServiceType == serviceType)
            ?? throw new InvalidOperationException($"Service of type {serviceType} not registered.");

        descriptor.Instance ??= Activator.CreateInstance(descriptor.ImplementationType);
        return descriptor.Instance;
    }

    public void MapGet(string route, Delegate handler) => RouteTable.Add($"GET {route}", handler);

    public void UseRouting() => Use(RouteHandler);

    public async Task RouteHandler(HttpContext context, RequestDelegate next)
    {
        string methodroute = $"{context.Request.Method} {context.Request.Route}";
        try
        {
            if (!RouteTable.ContainsKey(methodroute))
            {
                context.Response.StatusCode = HttpStatusCode.NotFound;
                context.Response.ResponseBody = "Not Found";
                await next(context);
                return;
            }
            await InvokeHandler(RouteTable[methodroute], context);
            Console.WriteLine("invoke done");
        }
        catch
        {
            context.Response.StatusCode = HttpStatusCode.InternalServerError;
            context.Response.ResponseBody = "Internal Server Error";
        }
    }

    public async Task InvokeHandler(Delegate handler, HttpContext ctx)
    {
        var resolved = handler.Method.GetParameters()
            .Select(p => p.ParameterType == typeof(HttpContext)
                ? ctx
                : GetService(p.ParameterType))
            .ToArray();

        var result = handler.DynamicInvoke(resolved);
        if (result is Task task) await task;
    }

    public void Use(Func<HttpContext, RequestDelegate, Task> middleware)
    {
        Func<RequestDelegate, RequestDelegate> factory = next => ctx => middleware(ctx, next);
        _middlewares.Add(factory);
    }

    public void Build()
    {
        RequestDelegate pipeline = _ => Task.CompletedTask;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
            pipeline = _middlewares[i](pipeline);
        app = pipeline;
    }

    public async Task Run()
    {
        while (true)
        {
            Socket client = await serverSocket.AcceptAsync();
            _ = HandleClient(client);         
        }
    }

    private async Task HandleClient(Socket client)
    {
        try
        {
            using (client)
            {
                var rawBytes = new List<byte>(4096);
                var tmp = new byte[4096];

                int headerEnd = -1;
                while (headerEnd == -1)
                {
                    int n = await client.ReceiveAsync(tmp, SocketFlags.None);
                    if (n == 0) return;
                    rawBytes.AddRange(tmp[..n]);

                    byte[] arr = rawBytes.ToArray();
                    for (int i = 0; i < arr.Length - 3; i++)
                    {
                        if (arr[i] == 13 && arr[i + 1] == 10 &&
                            arr[i + 2] == 13 && arr[i + 3] == 10)
                        {
                            headerEnd = i + 4; 
                            break;
                        }
                    }
                }

                byte[] raw = rawBytes.ToArray();
                string headerSection = Encoding.ASCII.GetString(raw, 0, headerEnd);

                string[] lines = headerSection.Split("\r\n", StringSplitOptions.None);

                string[] requestParts = lines[0].Split(' ');
                string method = requestParts[0];
                string route = requestParts[1];
                string version = requestParts[2];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line)) break;

                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;

                    string name = line[..colon].Trim();
                    string value = line[(colon + 1)..].Trim();
                    headers[name] = value;
                }

                string body = string.Empty;
                if (headers.TryGetValue("Content-Length", out string? clStr)
                    && int.TryParse(clStr, out int contentLength)
                    && contentLength > 0)
                {
                    byte[] bodyBytes = new byte[contentLength];
                    int alreadyHave = Math.Min(raw.Length - headerEnd, contentLength);
                    Array.Copy(raw, headerEnd, bodyBytes, 0, alreadyHave);

                    int remaining = contentLength - alreadyHave;
                    int offset = alreadyHave;
                    while (remaining > 0)
                    {
                        int n = await client.ReceiveAsync(
                            bodyBytes.AsMemory(offset, remaining), SocketFlags.None);
                        if (n == 0) break;
                        offset += n;
                        remaining -= n;
                    }
                    body = Encoding.UTF8.GetString(bodyBytes);
                }

                var context = new HttpContext
                {
                    Request = new DefaultHttpRequest
                    {
                        Method = method,
                        Route = route,
                        Version = version,
                        Headers = headers,
                        Body = body
                    },
                    Response = new DefaultHttpResponse()
                };

                await app.Invoke(context);

                int bodyLen = Encoding.UTF8.GetByteCount(context.Response.ResponseBody);
                string status = $"{(int)context.Response.StatusCode} {context.Response.StatusCode}";
                string res = $"HTTP/1.1 {status}\r\n" +
                                $"Content-Length: {bodyLen}\r\n" +
                                $"{context.Response.Header}\r\n\r\n" +
                                $"{context.Response.ResponseBody}";

                byte[] resBytes = Encoding.UTF8.GetBytes(res);
                Console.WriteLine(resBytes.Length);
                await client.SendAsync(resBytes, SocketFlags.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HandleClient] {ex.Message}");
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
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase); 
    public string Body { get; set; } = string.Empty;                                        
}

public class DefaultHttpResponse : IHttpResponseFeature
{
    public string ResponseBody { get; set; } = string.Empty;
    public string Header { get; set; }
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
}

public interface IHttpRequestFeature
{
    string Method { get; set; }
    string Route { get; set; }
    string Version { get; set; }
    Dictionary<string, string> Headers { get; set; } 
    string Body { get; set; }                 
}

public interface IHttpResponseFeature
{
    string ResponseBody { get; set; }
    string Header { get; set; }
    HttpStatusCode StatusCode { get; set; }
}