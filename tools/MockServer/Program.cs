// ぷようどんVS 動作確認用の簡易サーバー。
// 使い方: dotnet run --project tools/MockServer -- [ポート番号(既定 8080)] [待ち受けIP(既定 0.0.0.0)]
// ws://<IP>:<ポート>/udon で待ち受け、企画書 6.4 のメッセージを処理する。
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
var builder = WebApplication.CreateBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var bind = args.Length > 1 ? args[1] : "0.0.0.0";
builder.WebHost.UseUrls($"http://{bind}:{port}");
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });

var lobby = new Lobby();
app.Map("/udon", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var conn = new Conn(ws);
    Console.WriteLine($"[+] connect {ctx.Connection.RemoteIpAddress}");
    try { await conn.RunAsync(lobby); }
    catch (Exception ex) { Console.WriteLine($"[!] {ex.GetType().Name}: {ex.Message}"); }
    finally { lobby.Disconnect(conn); Console.WriteLine($"[-] disconnect {conn.Name}"); }
});
Console.WriteLine($"MockServer listening on ws://{bind}:{port}/udon");
app.Run();

sealed class Conn(WebSocket ws)
{
    public string Name = "";
    public Room? Room;
    public int Player;
    public bool WantsRematch;
    readonly SemaphoreSlim _sendLock = new(1, 1);
    long _lastRecv = Environment.TickCount64;

    public async Task SendAsync(object msg)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
        await _sendLock.WaitAsync();
        try { if (ws.State == WebSocketState.Open) await ws.SendAsync(bytes, WebSocketMessageType.Text, true, default); }
        catch { }
        finally { _sendLock.Release(); }
    }

    public void Send(object msg) => _ = SendAsync(msg);

    public async Task RunAsync(Lobby lobby)
    {
        using var cts = new CancellationTokenSource();
        // 15秒受信が無ければ切断とみなす
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1000);
                if (Environment.TickCount64 - Interlocked.Read(ref _lastRecv) > 15000) { ws.Abort(); return; }
            }
        });
        var buf = new byte[64 * 1024];
        var ms = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, default);
                if (r.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                Interlocked.Exchange(ref _lastRecv, Environment.TickCount64);
                var text = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);
                if (!lobby.Handle(this, text)) break;
            }
        }
        finally { cts.Cancel(); }
    }

    public void Close()
    {
        try { _ = ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", default); } catch { }
    }
}

sealed class Room(string id)
{
    public readonly string Id = id;
    public readonly Conn?[] Players = new Conn?[2];
    public bool Playing;
    public Conn? Other(Conn c) => Players[0] == c ? Players[1] : Players[0];
    public int Count => Players.Count(x => x != null);
}

sealed class Lobby
{
    readonly object _lock = new();
    readonly Dictionary<string, Room> _rooms = new();
    int _autoId;

    static object Err(int code, string message) => new { type = "ERROR", code, message };

    /// <summary>false を返すと接続を閉じる。</summary>
    public bool Handle(Conn c, string text)
    {
        JsonObject? m;
        try { m = JsonNode.Parse(text) as JsonObject; } catch { m = null; }
        var type = m?["type"]?.GetValue<string>();
        if (m == null || type == null) { c.Send(Err(1003, "invalid message")); return true; }
        lock (_lock)
        {
            var room = c.Room;
            var opp = room?.Other(c);
            switch (type)
            {
                case "PING":
                    c.Send(new { type = "PONG", t = m["t"]?.GetValue<long>() ?? 0 });
                    return true;
                case "JOIN":
                    {
                        if (room != null) { c.Send(Err(1004, "invalid state")); return true; }
                        var name = m["name"]?.GetValue<string>() ?? "";
                        var rid = m["room"]?.GetValue<string>() ?? "";
                        if (name.Trim().Length == 0 || name.Length > 10) { c.Send(Err(1002, "invalid name")); return true; }
                        Room r;
                        if (rid == "")
                        {
                            r = _rooms.Values.FirstOrDefault(x => x.Id.StartsWith("auto-") && x.Count == 1 && !x.Playing)
                                ?? NewRoom($"auto-{++_autoId}");
                        }
                        else if (!_rooms.TryGetValue(rid, out r!)) r = NewRoom(rid);
                        if (r.Count >= 2) { c.Send(Err(1001, "room is full")); return true; }
                        var slot = r.Players[0] == null ? 0 : 1;
                        r.Players[slot] = c;
                        c.Room = r; c.Player = slot + 1; c.Name = name; c.WantsRematch = false;
                        c.Send(new { type = "JOINED", player = c.Player, room = r.Id });
                        Console.WriteLine($"    JOIN {name} -> {r.Id} (P{c.Player})");
                        if (r.Count == 1) c.Send(new { type = "WAITING" });
                        else Start(r);
                        return true;
                    }
                case "PIECE":
                    if (room == null || !room.Playing) { c.Send(Err(1004, "invalid state")); return true; }
                    opp?.Send(new { type = "OPP_PIECE", x = m["x"]?.GetValue<int>() ?? 0, y = m["y"]?.GetValue<int>() ?? 0, rot = m["rot"]?.GetValue<int>() ?? 0 });
                    return true;
                case "STATE":
                    if (room == null || !room.Playing) { c.Send(Err(1004, "invalid state")); return true; }
                    opp?.Send(new { type = "OPP_STATE", board = m["board"]?.GetValue<string>() ?? "", chain = m["chain"]?.GetValue<int>() ?? 0 });
                    return true;
                case "CLEAR":
                    {
                        if (room == null || !room.Playing) { c.Send(Err(1004, "invalid state")); return true; }
                        var erased = m["erased"]?.GetValue<int>() ?? 0;
                        var chain = m["chain"]?.GetValue<int>() ?? 1;
                        var offset = Math.Max(0, m["offset"]?.GetValue<int>() ?? 0);
                        var count = Math.Max(0, Math.Max(1, erased * chain / 2) - offset);
                        Console.WriteLine($"    CLEAR {c.Name} erased={erased} chain={chain} offset={offset} -> GARBAGE {count}");
                        if (count > 0) opp?.Send(new { type = "GARBAGE", count });
                        return true;
                    }
                case "GAME_OVER":
                    if (room == null || !room.Playing) { c.Send(Err(1004, "invalid state")); return true; }
                    Finish(room, opp?.Player ?? (c.Player == 1 ? 2 : 1), "gameover");
                    return true;
                case "REMATCH":
                    if (room == null || room.Playing || opp == null) { c.Send(Err(1004, "invalid state")); return true; }
                    c.WantsRematch = true;
                    opp.Send(new { type = "OPP_REMATCH" });
                    if (opp.WantsRematch) Start(room);
                    return true;
                case "LEAVE":
                    Leave(c);
                    c.Close();
                    return false;
                default:
                    c.Send(Err(1003, "invalid message"));
                    return true;
            }
        }
    }

    Room NewRoom(string id) { var r = new Room(id); _rooms[id] = r; return r; }

    void Start(Room r)
    {
        var seed = Random.Shared.Next(1, 1_000_000);
        r.Playing = true;
        foreach (var pl in r.Players)
        {
            if (pl == null) continue;
            pl.WantsRematch = false;
            pl.Send(new { type = "START", seed, countdown = 3, opponent = r.Other(pl)?.Name ?? "" });
        }
        Console.WriteLine($"    START {r.Id} seed={seed}");
    }

    void Finish(Room r, int winner, string reason)
    {
        r.Playing = false;
        foreach (var pl in r.Players) pl?.Send(new { type = "RESULT", winner, reason });
        Console.WriteLine($"    RESULT {r.Id} winner=P{winner} ({reason})");
    }

    void Leave(Conn c)
    {
        var r = c.Room;
        if (r == null) return;
        var opp = r.Other(c);
        r.Players[c.Player - 1] = null;
        c.Room = null;
        if (opp != null)
        {
            opp.Send(new { type = "OPP_LEFT" });
            if (r.Playing) Finish(r, opp.Player, "left");
        }
        r.Playing = false;
        if (r.Count == 0) _rooms.Remove(r.Id);
    }

    public void Disconnect(Conn c) { lock (_lock) Leave(c); }
}
