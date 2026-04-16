using System.Net;
using System.Net.Sockets;
using System.Text;



TcpListener listener = new TcpListener(IPAddress.Any, 5050); //just stores the IP/port, no syscalls yet

NanoApplication app = new NanoApplication() { listener = listener };

//Takes ctx and next, returns a Task.
app.Use(async (ctx, next) => {
    Console.WriteLine("this is nano");
    await next(ctx);
});

app.Build();

await app.Run();


public class NanoApplication
{
    public List<Func<RequestDelegate, RequestDelegate>> _middlewares = new List<Func<RequestDelegate, RequestDelegate>>();

    public RequestDelegate app;

    public TcpListener listener;

    //Takes ctx and next, returns a Task. = Func<HttpContext, RequestDelegate, Task> = middleware(ctx, next)
    public void Use(Func<HttpContext, RequestDelegate, Task> middleware)
    {
        //Func<(next) = RequestDelegate , Func<(ctx) takes HttpContext, returns Task> = RequestDelegate >
        Func<RequestDelegate, RequestDelegate> factory = (next) => (ctx) => middleware(ctx, next);
        _middlewares.Add(factory);
    }

    public void Build()
    {
        RequestDelegate app = ctx => Task.CompletedTask;
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            app = _middlewares[i](app);
        }
        this.app = app;
    }

    public async Task Run()
    {
        listener.Start(); //socket() + bind() + listen().


        while (true)
        {
            //accept(), one connection socket per client.
            TcpClient client = await listener.AcceptTcpClientAsync();
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1000];
            int readable = await stream.ReadAsync(buffer, 0, 1000);

            byte[] requestline = new byte[900];
            int end = 0;
            for (int i = 0; i < readable - 1; i++)
            {
                if (buffer[i] == 13 && buffer[i + 1] == 10)
                {
                    Array.Copy(buffer, 0, requestline, 0, i);
                    end = i;
                    break;
                }
            }
            string line = Encoding.ASCII.GetString(requestline, 0, end);
            string[] parts = line.Split(' ');
            string Method = parts[0];
            string Route = parts[1];
            string Version = parts[2];

            HttpContext context = new HttpContext() { Request = new DefaultHttpRequest() { Method = Method, Route = Route, Version = Version }, Response = new DefaultHttpResponse() { } };

            await app.Invoke(context);

            string contentLength = Encoding.UTF8.GetByteCount(context.Response.ResponseBody).ToString();
            string resresponse = $"HTTP/1.1 {(int)context.Response.StatusCode} {context.Response.StatusCode}\r\nContent-Length: {contentLength}\r\n{context.Response.Header}\r\n\r\n{context.Response.ResponseBody}";

            byte[] resbuffer = Encoding.UTF8.GetBytes(resresponse);
            Console.WriteLine(resbuffer.Length);
            await stream.WriteAsync(resbuffer);
        }
    }
}
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
}

public interface IHttpResponseFeature
{
    public string ResponseBody { get; set; }
    public string Header { get; set; }
    public HttpStatusCode StatusCode { get; set; }
}


public delegate Task RequestDelegate(HttpContext context);


