//using System;
//using System.Collections.Generic;
//using System.Net;
//using System.Net.Sockets;
//using System.Text;

//namespace NanoApplication
//{

//    TcpListener listener = new TcpListener(IPAddress.Any, 5050); //just stores the IP/port, no syscalls yet

//    NanoApplication app = new NanoApplication() { listener = listener };

//    app.AddSingleton<IGreetingService, GreetingService>();

//app.MapGet("/hello", async(HttpContext ctx) => {
//    ctx.Response.ResponseBody = "Hello World!";
//    ctx.Response.Header = "Content-Type: text/plain";
//});

//app.MapGet("/hello1", (IGreetingService svc, HttpContext ctx) => svc.Greet(ctx));

////Takes ctx and next, returns a Task.
//app.Use(async (ctx, next) => {
//    Console.WriteLine("this is nano");
//    await next(ctx);
//});

//app.UseRouting();
//app.Build();

//await app.Run();

//public interface IGreetingService
//{
//    public void Greet(HttpContext ctx);
//}

//public class GreetingService : IGreetingService
//{
//    public void Greet(HttpContext ctx)
//    {
//        Console.WriteLine("hello! nano!");
//        ctx.Response.ResponseBody = "Hello Nano!";
//        ctx.Response.Header = "Content-Type: text/plain";
//    }
//}

//public class ServiceDescriptor
//{
//    public Type ServiceType { get; set; }
//    public Type ImplementationType { get; set; }
//    public Lifetime Lifetime { get; set; }
//    public object? Instance { get; set; }
//}

//public enum Lifetime
//{
//    Scoped,
//    Singleton,
//    Transient
//}

//public class ServiceCollection
//{
//    public List<ServiceDescriptor> serviceDescriptors { get; set; } = new List<ServiceDescriptor>();
//}
//public class NanoApplication
//{
//    public ServiceCollection _services { get; set; } = new ServiceCollection();

//    public List<Func<RequestDelegate, RequestDelegate>> _middlewares = new List<Func<RequestDelegate, RequestDelegate>>();

//    public RequestDelegate app;

//    public TcpListener listener;

//    public Dictionary<string, Delegate> RouteTable = new Dictionary<string, Delegate>();

//    public void AddSingleton<TService, TImplementation>()
//    {
//        var descriptor = new ServiceDescriptor() { ServiceType = typeof(TService), ImplementationType = typeof(TImplementation), Lifetime = Lifetime.Singleton };

//        this._services.serviceDescriptors.Add(descriptor);
//    }

//    public object GetService<TService>()
//    {
//        var descriptor = _services.serviceDescriptors
//        .FirstOrDefault(x => x.ServiceType == typeof(TService));

//        if (descriptor == null)
//            throw new InvalidOperationException($"Service of type {typeof(TService)} not registered.");

//        if (descriptor.Instance == null)
//            descriptor.Instance = Activator.CreateInstance(descriptor.ImplementationType);

//        return descriptor.Instance;
//    }

//    public object GetService(Type serviceType)
//    {
//        var descriptor = _services.serviceDescriptors
//        .FirstOrDefault(x => x.ServiceType == serviceType);

//        if (descriptor == null)
//            throw new InvalidOperationException($"Service of type {serviceType} not registered.");

//        if (descriptor.Instance == null)
//            descriptor.Instance = Activator.CreateInstance(descriptor.ImplementationType);

//        return descriptor.Instance;
//    }

//    public void MapGet(string route, Delegate handler)
//    {
//        RouteTable.Add($"GET {route}", handler);
//    }
//    public void UseRouting()
//    {
//        this.Use(RouteHandler);
//    }
//    public async Task RouteHandler(HttpContext context, RequestDelegate next)
//    {
//        string methodroute = $"{context.Request.Method} {context.Request.Route}";

//        try
//        {
//            if (!RouteTable.ContainsKey(methodroute))
//            {
//                context.Response.StatusCode = HttpStatusCode.NotFound;
//                context.Response.ResponseBody = "Not Found";

//                await next(context);
//                return;
//            }
//            await InvokeHandler(RouteTable[methodroute], context);

//            Console.WriteLine("invoke done");
//        }
//        catch (Exception)
//        {
//            context.Response.StatusCode = HttpStatusCode.InternalServerError;
//            context.Response.ResponseBody = "Internal Server Error";
//            return;
//        }

//    }

//    public async Task InvokeHandler(Delegate handler, HttpContext ctx)
//    {
//        var toResolve = handler.Method.GetParameters().ToList();

//        var resolved = new List<object>();
//        foreach (var parameterInfo in toResolve)
//        {
//            if (parameterInfo.ParameterType == typeof(HttpContext))
//            {
//                resolved.Add(ctx);
//            }
//            else
//            {
//                resolved.Add(GetService(parameterInfo.ParameterType));
//            }
//        }

//        var result = handler.DynamicInvoke(resolved.ToArray());

//        if (result is Task task)
//            await task;

//        return;
//    }

//    //Takes ctx and next, returns a Task. = Func<HttpContext, RequestDelegate, Task> = middleware(ctx, next)
//    public void Use(Func<HttpContext, RequestDelegate, Task> middleware)
//    {
//        //Func<(next) = RequestDelegate , Func<(ctx) takes HttpContext, returns Task> = RequestDelegate >
//        Func<RequestDelegate, RequestDelegate> factory = (next) => (ctx) => middleware(ctx, next);

//        // Func<RequestDelegate, RequestDelegate> factory = 
//        // function(next)
//        // {
//        //     return function(ctx)
//        //     {
//        //         return middleware(ctx, next);
//        //     };
//        // };

//        _middlewares.Add(factory);
//    }


//    public void Build()
//    {
//        RequestDelegate app = ctx => Task.CompletedTask;
//        for (int i = _middlewares.Count - 1; i >= 0; i--)
//        {
//            app = _middlewares[i](app);
//        }
//        this.app = app;
//    }

//    public async Task Run()
//    {
//        listener.Start(); //socket() + bind() + listen().


//        while (true)
//        {
//            //accept(), one connection socket per client.
//            TcpClient client = await listener.AcceptTcpClientAsync();
//            NetworkStream stream = client.GetStream();
//            byte[] buffer = new byte[1000];
//            int readable = await stream.ReadAsync(buffer, 0, 1000);

//            byte[] requestline = new byte[900];
//            int end = 0;
//            for (int i = 0; i < readable - 1; i++)
//            {
//                if (buffer[i] == 13 && buffer[i + 1] == 10)
//                {
//                    Array.Copy(buffer, 0, requestline, 0, i);
//                    end = i;
//                    break;
//                }
//            }
//            string line = Encoding.ASCII.GetString(requestline, 0, end);
//            string[] parts = line.Split(' ');
//            string Method = parts[0];
//            string Route = parts[1];
//            string Version = parts[2];

//            HttpContext context = new HttpContext() { Request = new DefaultHttpRequest() { Method = Method, Route = Route, Version = Version }, Response = new DefaultHttpResponse() { } };

//            await app.Invoke(context);

//            string contentLength = Encoding.UTF8.GetByteCount(context.Response.ResponseBody).ToString();
//            string resresponse = $"HTTP/1.1 {(int)context.Response.StatusCode} {context.Response.StatusCode}\r\nContent-Length: {contentLength}\r\n{context.Response.Header}\r\n\r\n{context.Response.ResponseBody}";

//            byte[] resbuffer = Encoding.UTF8.GetBytes(resresponse);
//            Console.WriteLine(resbuffer.Length);
//            await stream.WriteAsync(resbuffer);
//        }
//    }
//}



//public delegate Task RequestDelegate(HttpContext context);


//public class HttpContext
//{
//    public IHttpRequestFeature Request { get; set; }
//    public IHttpResponseFeature Response { get; set; }
//}

//public class DefaultHttpRequest : IHttpRequestFeature
//{
//    public string Method { get; set; }
//    public string Route { get; set; }
//    public string Version { get; set; }
//}
//public class DefaultHttpResponse : IHttpResponseFeature
//{
//    public string ResponseBody { get; set; } = string.Empty;
//    public string Header { get; set; }
//    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
//}
//public interface IHttpRequestFeature
//{
//    public string Method { get; set; }
//    public string Route { get; set; }
//    public string Version { get; set; }
//}

//public interface IHttpResponseFeature
//{
//    public string ResponseBody { get; set; }
//    public string Header { get; set; }
//    public HttpStatusCode StatusCode { get; set; }
//}

//}
