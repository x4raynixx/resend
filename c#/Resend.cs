using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace resend
{
    public class ResendRoute
    {
        public string Name { get; set; }
        public string Route { get; set; }
    }

    public class ResendConfig
    {
        public List<string> AllowConnectionsFrom { get; set; } = new();
        public bool GlobalAccess { get; set; } = true;
        public List<ResendRoute> Routes { get; set; } = new();
        public bool AllowJson { get; set; } = true;
        public bool EnableLogs { get; set; } = false;
    }

    public static class Resend
    {
        private static readonly HttpListener listener = new();
        private static readonly ConcurrentDictionary<Guid, WebSocket> clients = new();
        private static readonly Dictionary<string, Func<string, string>> handlers = new();
        private static ResendConfig config;

        public static void Configure(ResendConfig cfg)
        {
            config = cfg;
        }

        public static void On(string route, Func<string, string> callback)
        {
            handlers[route] = callback;
        }

        public static async Task StartAsync(int port)
        {
            listener.Prefixes.Add($"http://localhost:{port}/");

            listener.Start();
            if (config.EnableLogs)
            {
                Console.WriteLine($"[resend] HTTP+WS server on http://localhost:{port}");
            }

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var context = await listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                        _ = HandleConnection(context);
                    else
                        context.Response.StatusCode = 400;
                }
            });
        }

        private static async Task HandleConnection(HttpListenerContext context)
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            var ws = wsContext.WebSocket;
            var id = Guid.NewGuid();
            clients.TryAdd(id, ws);
            if (config.EnableLogs)
            {
                Console.WriteLine("[resend] WebSocket client connected");
            }

            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(msg);
                    if (data != null && data.TryGetValue("route", out var route) && data.TryGetValue("data", out var payload))
                    {
                        string response = handlers.ContainsKey(route) ? handlers[route].Invoke(payload) : payload;

                        var jsonResponse = JsonSerializer.Serialize(new
                        {
                            route,
                            data = response
                        });

                        var bytes = Encoding.UTF8.GetBytes(jsonResponse);
                        foreach (var kv in clients)
                        {
                            if (kv.Value.State == WebSocketState.Open)
                            {
                                await kv.Value.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                        }
                    }
                }
                catch
                {
                    var errorResponse = JsonSerializer.Serialize(new
                    {
                        statusCode = 400,
                        message = "Invalid data format"
                    });
                    var errorBytes = Encoding.UTF8.GetBytes(errorResponse);
                    await SendErrorToClients(errorBytes);
                    return;
                }
            }

            clients.TryRemove(id, out _);
        }

        private static async Task SendErrorToClients(byte[] errorBytes)
        {
            foreach (var kv in clients)
            {
                if (kv.Value.State == WebSocketState.Open)
                {
                    await kv.Value.SendAsync(new ArraySegment<byte>(errorBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
    }
}
