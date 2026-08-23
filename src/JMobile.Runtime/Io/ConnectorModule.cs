using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Host;
using JMobile.Runtime.Midp;

namespace JMobile.Runtime.Io;

/// <summary>
/// The Generic Connection Framework, in the form MIDP games actually use it: HTTP, and reading
/// resources out of the suite.
/// </summary>
/// <remarks>
/// Network access is off unless the host enables it. Most of these games talk to servers that
/// were switched off years ago, and the MIDP API is blocking, so an enabled-by-default network
/// stack would mean a game hanging the event thread on a DNS lookup that can only fail. When
/// disabled, <c>Connector.open</c> fails immediately with the <c>IOException</c> the game
/// already knows how to handle.
/// </remarks>
public sealed class ConnectorModule : IRuntimeModule
{
    public string Name => "javax.microedition.io";

    private const int ConnectionSlot = 0;

    public void Register(RuntimeRegistry registry)
    {
        RegisterInterfaces(registry);
        RegisterConnector(registry);
        RegisterHttpConnection(registry);
    }

    private static void RegisterInterfaces(RuntimeRegistry registry)
    {
        registry.DefineThrowable("javax/microedition/io/ConnectionNotFoundException", WellKnown.IOException);

        registry.DefineInterface("javax/microedition/io/Connection", builder =>
            builder.InterfaceMethod("close", "()V"));

        registry.DefineInterface("javax/microedition/io/InputConnection", builder => builder
            .Implements("javax/microedition/io/Connection")
            .InterfaceMethod("openInputStream", "()Ljava/io/InputStream;")
            .InterfaceMethod("openDataInputStream", "()Ljava/io/DataInputStream;"));

        registry.DefineInterface("javax/microedition/io/OutputConnection", builder => builder
            .Implements("javax/microedition/io/Connection")
            .InterfaceMethod("openOutputStream", "()Ljava/io/OutputStream;")
            .InterfaceMethod("openDataOutputStream", "()Ljava/io/DataOutputStream;"));

        registry.DefineInterface("javax/microedition/io/StreamConnection", builder =>
            builder.Implements("javax/microedition/io/InputConnection", "javax/microedition/io/OutputConnection"));

        registry.DefineInterface("javax/microedition/io/ContentConnection", builder => builder
            .Implements("javax/microedition/io/StreamConnection")
            .InterfaceMethod("getType", "()Ljava/lang/String;")
            .InterfaceMethod("getEncoding", "()Ljava/lang/String;")
            .InterfaceMethod("getLength", "()J"));

        registry.DefineInterface("javax/microedition/io/HttpConnection", builder => builder
            .Implements("javax/microedition/io/ContentConnection")
            .Constant("HTTP_OK", 200)
            .Constant("HTTP_CREATED", 201)
            .Constant("HTTP_ACCEPTED", 202)
            .Constant("HTTP_NO_CONTENT", 204)
            .Constant("HTTP_MOVED_PERM", 301)
            .Constant("HTTP_MOVED_TEMP", 302)
            .Constant("HTTP_NOT_MODIFIED", 304)
            .Constant("HTTP_BAD_REQUEST", 400)
            .Constant("HTTP_UNAUTHORIZED", 401)
            .Constant("HTTP_FORBIDDEN", 403)
            .Constant("HTTP_NOT_FOUND", 404)
            .Constant("HTTP_INTERNAL_ERROR", 500)
            .Constant("HTTP_UNAVAILABLE", 503)
            .InterfaceMethod("getResponseCode", "()I")
            .InterfaceMethod("getResponseMessage", "()Ljava/lang/String;")
            .InterfaceMethod("setRequestMethod", "(Ljava/lang/String;)V")
            .InterfaceMethod("setRequestProperty", "(Ljava/lang/String;Ljava/lang/String;)V")
            .InterfaceMethod("getHeaderField", "(Ljava/lang/String;)Ljava/lang/String;"));
    }

    private static void RegisterConnector(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/io/Connector", builder => builder
            .Constant("READ", 1)
            .Constant("WRITE", 2)
            .Constant("READ_WRITE", 3)
            .StaticMethod("open", "(Ljava/lang/String;)Ljavax/microedition/io/Connection;",
                static (thread, args) => JavaValue.Ref(Open(thread, args.String(0))))
            .StaticMethod("open", "(Ljava/lang/String;I)Ljavax/microedition/io/Connection;",
                static (thread, args) => JavaValue.Ref(Open(thread, args.String(0))))
            .StaticMethod("open", "(Ljava/lang/String;IZ)Ljavax/microedition/io/Connection;",
                static (thread, args) => JavaValue.Ref(Open(thread, args.String(0))))
            .StaticMethod("openInputStream", "(Ljava/lang/String;)Ljava/io/InputStream;",
                static (thread, args) =>
                {
                    JavaObject connection = Open(thread, args.String(0));
                    return thread.Vm.CallVirtual(thread, connection, "openInputStream",
                        "()Ljava/io/InputStream;");
                })
            .StaticMethod("openDataInputStream", "(Ljava/lang/String;)Ljava/io/DataInputStream;",
                static (thread, args) =>
                {
                    JavaObject connection = Open(thread, args.String(0));
                    return thread.Vm.CallVirtual(thread, connection, "openDataInputStream",
                        "()Ljava/io/DataInputStream;");
                }));
    }

    private static JavaObject Open(JavaThread thread, string? url)
    {
        string locator = RuntimeSupport.RequireString(thread, url, "name");

        if (locator.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            locator.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            INetworkHost? network = MidpRuntime.Of(thread.Vm).Network;
            if (network is null || !network.IsEnabled)
            {
                throw thread.Vm.NewException(thread, "javax/microedition/io/ConnectionNotFoundException",
                    "network access is disabled for this game");
            }

            JavaClass type = thread.Vm.LoadClass("javax/microedition/io/HttpConnectionImpl");
            var connection = (JavaConnection)thread.Vm.NewInstance(type);
            connection.Url = locator;
            connection.IsConnected = false;
            return connection;
        }

        throw thread.Vm.NewException(thread, "javax/microedition/io/ConnectionNotFoundException",
            "unsupported protocol: " + locator);
    }

    private static void RegisterHttpConnection(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/io/HttpConnectionImpl", builder => builder
            .Implements("javax/microedition/io/HttpConnection")
            .AllocatesWith(static type => new JavaConnection(type, type.InstanceFieldCount))
            .Method("setRequestMethod", "(Ljava/lang/String;)V", static (_, args) =>
            {
                args.Self<JavaConnection>().RequestMethod = args.String(0) ?? "GET";
                return JavaValue.Void;
            })
            .Method("getRequestMethod", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaConnection>().RequestMethod))
            .Method("setRequestProperty", "(Ljava/lang/String;Ljava/lang/String;)V", static (_, args) =>
            {
                if (args.String(0) is { } key)
                    args.Self<JavaConnection>().RequestHeaders.Add(new(key, args.String(1) ?? string.Empty));
                return JavaValue.Void;
            })
            .Method("getURL", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaConnection>().Url))
            .Method("getResponseCode", "()I", static (thread, args) =>
                JavaValue.Int(Connect(thread, args.Self<JavaConnection>()).StatusCode))
            .Method("getResponseMessage", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, Connect(thread, args.Self<JavaConnection>()).StatusMessage))
            .Method("getLength", "()J", static (thread, args) =>
                JavaValue.Long(Connect(thread, args.Self<JavaConnection>()).Body.Length))
            .Method("getType", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, Header(Connect(thread, args.Self<JavaConnection>()), "Content-Type")))
            .Method("getEncoding", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread,
                    Header(Connect(thread, args.Self<JavaConnection>()), "Content-Encoding")))
            .Method("getHeaderField", "(Ljava/lang/String;)Ljava/lang/String;", static (thread, args) =>
            {
                string? value = Header(Connect(thread, args.Self<JavaConnection>()), args.String(0) ?? "");
                return RuntimeSupport.Str(thread, value);
            })
            .Method("openInputStream", "()Ljava/io/InputStream;", static (thread, args) =>
            {
                HttpResult result = Connect(thread, args.Self<JavaConnection>());
                return JavaValue.Ref(IoModule.NewByteArrayInputStream(thread, result.Body));
            })
            .Method("openDataInputStream", "()Ljava/io/DataInputStream;", static (thread, args) =>
            {
                HttpResult result = Connect(thread, args.Self<JavaConnection>());
                JavaObject stream = IoModule.NewByteArrayInputStream(thread, result.Body);
                return JavaValue.Ref(thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("java/io/DataInputStream"),
                    "(Ljava/io/InputStream;)V", JavaValue.Ref(stream)));
            })
            .Method("openOutputStream", "()Ljava/io/OutputStream;", static (thread, args) =>
            {
                // Writes accumulate into the request body and are sent when the response is
                // first read, which is the point at which MIDP says the request is committed.
                var self = args.Self<JavaConnection>();
                JavaClass type = thread.Vm.LoadClass("java/io/ByteArrayOutputStream");
                var stream = (JavaStream)thread.Vm.NewInstance(type);
                stream.Underlying = self.RequestBody;
                return JavaValue.Ref(stream);
            })
            .Method("openDataOutputStream", "()Ljava/io/DataOutputStream;", static (thread, args) =>
            {
                var self = args.Self<JavaConnection>();
                JavaClass byteArrayType = thread.Vm.LoadClass("java/io/ByteArrayOutputStream");
                var inner = (JavaStream)thread.Vm.NewInstance(byteArrayType);
                inner.Underlying = self.RequestBody;

                return JavaValue.Ref(thread.Vm.Construct(
                    thread, thread.Vm.LoadClass("java/io/DataOutputStream"),
                    "(Ljava/io/OutputStream;)V", JavaValue.Ref(inner)));
            })
            .Method("close", "()V", static (_, args) =>
            {
                args.Self<JavaConnection>().IsConnected = false;
                return JavaValue.Void;
            }));
    }

    /// <summary>
    /// Performs the request if it has not run yet, and returns the memoised response.
    /// </summary>
    /// <remarks>
    /// MIDP's <c>HttpConnection</c> makes the request lazily, on the first call that needs a
    /// response, and every later call sees the same one. Memoising here is what makes a game
    /// reading the status code and then the body issue a single request.
    /// </remarks>
    private static HttpResult Connect(JavaThread thread, JavaConnection connection)
    {
        if (connection.Response is { } cached) return cached;

        INetworkHost? network = MidpRuntime.Of(thread.Vm).Network;
        if (network is null || !network.IsEnabled)
            throw thread.Vm.NewIOException(thread, "network access is disabled");

        var request = new HttpRequest(
            connection.Url,
            connection.RequestMethod,
            connection.RequestHeaders,
            connection.RequestBody.ToArray());

        HttpResult result;
        try
        {
            result = network.Request(request);
        }
        catch (Exception error)
        {
            throw thread.Vm.NewIOException(thread, error.Message);
        }

        if (result.StatusCode == 0) throw thread.Vm.NewIOException(thread, result.StatusMessage);

        connection.Response = result;
        connection.IsConnected = true;
        return result;
    }

    private static string? Header(HttpResult result, string name)
    {
        foreach (KeyValuePair<string, string> header in result.Headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) return header.Value;
        }
        return null;
    }
}
