# Resend

Resend is a fast and efficient module for creating servers that listen for incoming connections and send data across networks.

## Installation

1. **Download the DLL** from the [Releases](https://github.com/x4raynixx/resend/releases).
2. **Add the DLL** to your project (e.g., place it in a folder).
3. **Reference the DLL** in your project by right-clicking your project > **Add > Reference** > Browse to the DLL.

## Usage

### 1. Configure the Server

```csharp
using Resend;

public class Program
{
    public static void Main(string[] args)
    {
        Resend.Configure(new ResendConfig
        {
            GlobalAccess = true,
            Routes = new List<ResendRoute> { new ResendRoute { Name = "chat", Route = "chat/send" } }
        });

        Resend.StartAsync(5000).Wait();
    }
}
```

### 2. Define Routes

```csharp
Resend.On("chat/send", (message) =>
{
    return $"Echo: {message}";
});
```

### 3. Sending Messages

```csharp
static async Task SendMessageToServer(string message)
{
    var socket = new System.Net.WebSockets.ClientWebSocket();
    await socket.ConnectAsync(new Uri("ws://localhost:5000/ws/"), CancellationToken.None);

    string jsonMessage = $"{{\"route\": \"chat/send\", \"data\": \"{message}\"}}";
    byte[] messageBytes = Encoding.UTF8.GetBytes(jsonMessage);
    await socket.SendAsync(new ArraySegment<byte>(messageBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);

    byte[] buffer = new byte[4096];
    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    string response = Encoding.UTF8.GetString(buffer, 0, result.Count);
    Console.WriteLine($"Server response: {response}");

    socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
}
```

## Summary

1. **Download and reference the DLL**.
2. **Configure the server** with routes.
3. **Send messages** via WebSocket.
