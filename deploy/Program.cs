using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Specialized;

struct Vector2
{
    public float X, Y;
    public Vector2(float x, float y) { X = x; Y = y; }
}

class Health
{
    public int Id;
    public float X, Y;
    public DateTime SpawnTime;
}

class Tank
{
    public int Id;
    public float X, Y;
    public string Name;
    public int DirX = 0, DirY = -1;
    public int HP = 100;
    public DateTime LastShotTime;
}

class Bullet
{
    public int Id;
    public int OwnerId;
    public float X, Y;
    public float HX, HY;
    public int DirX, DirY;
    public float Speed = 500f;
    public bool Alive = true;
}

class GameMap
{
    public const int TILE = 32;
    int[,] grid;
    int w, h;

    public GameMap(int width, int height)
    {
        w = width;
        h = height;
        grid = new int[h, w];

        for (int x = 0; x < w; x++)
        {
            grid[0, x] = 1;
            grid[h - 1, x] = 1;
        }
        for (int y = 0; y < h; y++)
        {
            grid[y, 0] = 1;
            grid[y, w - 1] = 1;
        }
        for (int y = 4; y < h - 2; y += 8)
            for (int x = 4; x < w - 2; x += 8)
            {
                grid[y, x] = 1;
                grid[y + 1, x] = 1;
                grid[y, x + 1] = 1;
                grid[y + 1, x + 1] = 1;
            }
    }

    bool IsSolidTile(int tx, int ty)
    {
        if (tx < 0 || ty < 0 || tx >= w || ty >= h)
            return true;
        return grid[ty, tx] == 1;
    }

    public bool BoxHitsWall(float x, float y, float w, float h)
    {
        int left = (int)Math.Floor(x / TILE);
        int right = (int)Math.Floor((x + w - 0.01f) / TILE);
        int top = (int)Math.Floor(y / TILE);
        int bottom = (int)Math.Floor((y + h - 0.01f) / TILE);

        for (int ty = top; ty <= bottom; ty++)
            for (int tx = left; tx <= right; tx++)
                if (IsSolidTile(tx, ty))
                    return true;

        return false;
    }
}

class Server
{
    const float MAP_WIDTH = 1280f;
    const float MAP_HEIGHT = 704f;

    const float TANK_SIZE = 38f;
    const float TANK_HITBOX = 28;

    const float BULLET_SPRITE = 32f;
    const float BULLET_HITBOX = 8f;

    static TcpListener listener;
    static Dictionary<TcpClient, Tank> clientTanks = new();
    static List<TcpClient> clients = new();
    static List<Bullet> bullets = new();
    static double ExistLim = 10;
    static List<Health> healths = new();
    static Dictionary<int, int> scores = new();

    static GameMap gameMap = new(40, 22);
    static Random rand = new();
    static object lockObj = new();

    static int nextTankId = 1;
    static int nextBulletId = 1;
    static int nextHealthId = 1;

    static void Main()
    {
        var ipv4 = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        Console.WriteLine($"Server IP: {ipv4}");
        Console.Write("PORT (default 3636): ");
        //int port = int.TryParse(Console.ReadLine(), out int p) ? p : 3636;
        int port = 9000;
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Server started on port {port}");

        new Thread(GameLoop) { IsBackground = true }.Start();

        while (true)
        {
            TcpClient c = listener.AcceptTcpClient();
            lock (lockObj)
            {
                Vector2 spawn = FindSpawn();
                Tank t = new()
                {
                    Id = nextTankId++,
                    X = spawn.X,
                    Y = spawn.Y,
                    Name = RandomTankName()
                };
                clientTanks[c] = t;
                clients.Add(c);
                Console.WriteLine($"Tank {t.Id} connected");
            }
            new Thread(HandleClient) { IsBackground = true }.Start(c);
        }
    }
    static void SpawnHealth()
    {
        Vector2 spawn = FindSpawn();
        Health h = new()
        {
            Id = nextHealthId++,
            X = spawn.X,
            Y = spawn.Y,
            SpawnTime = DateTime.Now
        };
        healths.Add(h);
    }
    static Vector2 FindSpawn()
    {
        while (true)
        {
            float x = rand.Next(100, (int)(MAP_WIDTH - 100));
            float y = rand.Next(100, (int)(MAP_HEIGHT - 100));
            if (!gameMap.BoxHitsWall(x, y, TANK_SIZE, TANK_SIZE))
                return new Vector2(x, y);
        }
    }
    static string RandomTankName()
    {
        string[] a =
        {
        "Do Mixi", "Big", "Fire", "Ash", "Cute",
        "Chill", "36", "Ronado", "Long", "Messi"
    };

        string[] b =
        {
        "Bob", "Dik", "Dat", "Thanh Hoa", "EAI",
        "Guy", "Shot", "lowg", "psy", "Ahs"
    };

        return a[rand.Next(a.Length)] + " " +
               b[rand.Next(b.Length)] + " " +
               rand.Next(1, 99);
    }

    static void HandleClient(object obj)
    {
        TcpClient c = (TcpClient)obj;
        NetworkStream s = c.GetStream();
        byte[] buf = new byte[256];

        try
        {
            while (true)
            {
                int n;
                try
                {
                    n = s.Read(buf, 0, buf.Length);
                }
                catch (IOException)
                {
                    // client disconnect bẩn (Alt+F4, mất mạng)
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                if (n <= 0)
                    break; // disconnect sạch

                string cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();

                lock (lockObj)
                {
                    if (!clientTanks.ContainsKey(c))
                        break;

                    Tank t = clientTanks[c];
                    float speed = 100f / 30f;
                    if (cmd.StartsWith("NAME:"))
                    {
                        string name = cmd.Substring(5).Trim();

                        if (string.IsNullOrEmpty(name) || name == "0")
                            t.Name = RandomTankName();
                        else
                            t.Name = name;

                        continue;
                    }

                    if (cmd == "1") { t.DirX = 0; t.DirY = -1; MoveTank(t, 0, -speed); }
                    if (cmd == "2") { t.DirX = -1; t.DirY = 0; MoveTank(t, -speed, 0); }
                    if (cmd == "3") { t.DirX = 0; t.DirY = 1; MoveTank(t, 0, speed); }
                    if (cmd == "4") { t.DirX = 1; t.DirY = 0; MoveTank(t, speed, 0); }
                    if (cmd == "0") Shoot(t);
                }
            }
        }
        finally
        {
            lock (lockObj)
            {
                if (clientTanks.TryGetValue(c, out var t))
                    Console.WriteLine($"Tank {t.Id} disconnected");

                clientTanks.Remove(c);
                clients.Remove(c);
                scores.Remove(t.Id);
            }

            try { s.Close(); } catch { }
            try { c.Close(); } catch { }
        }
    }


    static void MoveTank(Tank t, float dx, float dy)
    {
        float nx = t.X + dx;
        float ny = t.Y + dy;

        /*if (!gameMap.BoxHitsWall(nx, t.Y, TANK_SIZE, TANK_SIZE))
            t.X = Math.Clamp(nx, 0, MAP_WIDTH - TANK_SIZE);

        if (!gameMap.BoxHitsWall(t.X, ny, TANK_SIZE, TANK_SIZE))
            t.Y = Math.Clamp(ny, 0, MAP_HEIGHT - TANK_SIZE);*/
        float ox = (TANK_SIZE - TANK_HITBOX) / 2;
        float oy = (TANK_SIZE - TANK_HITBOX) / 2;

        if (!gameMap.BoxHitsWall(nx + ox, t.Y + oy, TANK_HITBOX, TANK_HITBOX))
            t.X = Math.Clamp(nx, 0, MAP_WIDTH - TANK_SIZE);

        if (!gameMap.BoxHitsWall(t.X + ox, ny + oy, TANK_HITBOX, TANK_HITBOX))
            t.Y = Math.Clamp(ny, 0, MAP_HEIGHT - TANK_SIZE);

    }
    static readonly Dictionary<(int, int), Vector2> BulletVisualOffset =
    new()
    {
        {(0, -1), new Vector2(0, 0)},   //up
        {(0,  1), new Vector2(0, 0)},  //down
        {(-1, 0), new Vector2(0, -5)}, //left
        {( 1, 0), new Vector2(0, -5)},  //right
    };

    static void Shoot(Tank t)
    {
        if ((DateTime.Now - t.LastShotTime).TotalSeconds < 0.25) return;
        if (t.DirX == 0 && t.DirY == 0) t.DirY = -1;

        float cx = t.X + TANK_SIZE / 2;
        float cy = t.Y + TANK_SIZE / 2;
        float offset = TANK_SIZE / 2 + 6f;

        // HITBOX CENTER (LOGIC)
        float hx = cx + t.DirX * offset - BULLET_HITBOX / 2;
        float hy = cy + t.DirY * offset - BULLET_HITBOX / 2;

        if (gameMap.BoxHitsWall(hx, hy, BULLET_HITBOX, BULLET_HITBOX)) return;

        // VISUAL OFFSET (SPRITE)
        Vector2 vo = BulletVisualOffset[(t.DirX, t.DirY)];
        //Console.WriteLine(vo.X+" "+ vo.Y);
        float sx = hx - (BULLET_SPRITE - BULLET_HITBOX) / 2 + vo.X;
        float sy = hy - (BULLET_SPRITE - BULLET_HITBOX) / 2 + vo.Y;

        bullets.Add(new Bullet
        {
            Id = nextBulletId++,
            OwnerId = t.Id,
            X = sx,
            Y = sy,
            HX = hx,
            HY = hy,
            DirX = t.DirX,
            DirY = t.DirY
        });

        t.LastShotTime = DateTime.Now;
    }


    static void GameLoop()
    {
        const float dt = 1f / 30f;

        while (true)
        {
            Thread.Sleep(33);
            lock (lockObj)
            {
                UpdateBullets(dt);
                UpdateHealth();
                ResolveTankCollisions();
                Broadcast();
            }
        }
    }
    static void UpdateHealth()
    {
        int value = rand.Next(0, 2);
        if (value == 1 && healths.Count < 2)
        {
            SpawnHealth();
        }
        List<Health> removeList = new List<Health>();

        foreach (var h in healths)
        {
            double ExistTime = (DateTime.Now - h.SpawnTime).TotalSeconds;
            bool Used = false;

            if (ExistTime > ExistLim)
            {
                Used = true;
            }
            else
            {
                foreach (var t in clientTanks.Values)
                {
                    if (RectIntersect(
                        h.X, h.Y, 32, 32,
                        t.X, t.Y, TANK_SIZE, TANK_SIZE))
                    {
                        t.HP = Math.Min(t.HP + 50, 100);
                        Used = true;
                        break;
                    }
                }
            }

            if (Used)
                removeList.Add(h);
        }

        // xóa sau
        foreach (var h in removeList)
        {
            healths.Remove(h);
        }
    }
    static void UpdateBullets(float dt)
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = bullets[i];
            float dist = b.Speed * dt;
            int steps = (int)(dist / 6f) + 1;

            float sx = b.DirX * dist / steps;
            float sy = b.DirY * dist / steps;

            for (int s = 0; s < steps; s++)
            {
                Vector2 vo = BulletVisualOffset[(b.DirX, b.DirY)];
                //float nx = b.X + sx;
                //float ny = b.Y + sy;

                /*float hx = nx + (BULLET_SPRITE - BULLET_HITBOX) / 2;
                float hy = ny + (BULLET_SPRITE - BULLET_HITBOX) / 2;*/
                b.HX += sx;
                b.HY += sy;
                float hx = b.HX;
                float hy = b.HY;

                b.X = b.HX - (BULLET_SPRITE - BULLET_HITBOX) / 2 + vo.X;
                b.Y = b.HY - (BULLET_SPRITE - BULLET_HITBOX) / 2 + vo.Y;
                if (gameMap.BoxHitsWall(hx, hy, BULLET_HITBOX, BULLET_HITBOX))
                {
                    bullets.RemoveAt(i);
                    goto NEXT;
                }

                foreach (var t in clientTanks.Values)
                {
                    if (t.Id == b.OwnerId) continue;

                    if (hx < t.X + TANK_SIZE &&
                        hx + BULLET_HITBOX > t.X &&
                        hy < t.Y + TANK_SIZE &&
                        hy + BULLET_HITBOX > t.Y)
                    {
                        t.HP -= 10;
                        bullets.RemoveAt(i);

                        if (t.HP <= 0)
                        {
                            scores[b.OwnerId] = scores.GetValueOrDefault(b.OwnerId) + 1;
                            Vector2 sp = FindSpawn();
                            t.X = sp.X; t.Y = sp.Y; t.HP = 100;
                        }
                        goto NEXT;
                    }
                }

                //b.X = nx;
                //b.Y = ny;
            }
        NEXT:;
        }
    }
    static bool CanPushTank(Tank self, Tank ignore, float newX, float newY)
    {
        if (gameMap.BoxHitsWall(newX, newY, TANK_SIZE, TANK_SIZE))
            return false;

        foreach (var other in clientTanks.Values)
        {
            if (other == self) continue;
            if (other == ignore) continue;

            if (RectIntersect(
                newX, newY, TANK_SIZE, TANK_SIZE,
                other.X, other.Y, TANK_SIZE, TANK_SIZE))
                return false;
        }

        return true;
    }

    static bool RectIntersect(
    float x1, float y1, float w1, float h1,
    float x2, float y2, float w2, float h2)
    {
        float aL = x1;
        float aR = x1 + TANK_SIZE;
        float aT = y1;
        float aB = y1 + TANK_SIZE;

        float bL = x2;
        float bR = x2 + TANK_SIZE;
        float bT = y2;
        float bB = y2 + TANK_SIZE;

        if (aL < bR && aR > bL && aT < bB && aB > bT)
        {
            return true;
        }
        return false;
    }
    static void ResolveTankCollisions()
    {
        var tanks = clientTanks.Values.ToArray();

        for (int i = 0; i < tanks.Length; i++)
            for (int j = i + 1; j < tanks.Length; j++)
            {
                Tank a = tanks[i];
                Tank b = tanks[j];

                if (RectIntersect(
                    a.X, a.Y, TANK_SIZE, TANK_SIZE,
                    b.X, b.Y, TANK_SIZE, TANK_SIZE) == false)
                    continue;
                //Console.WriteLine("run");
                float overlapX = Math.Min(
                    a.X + TANK_SIZE - b.X,
                    b.X + TANK_SIZE - a.X);

                float overlapY = Math.Min(
                    a.Y + TANK_SIZE - b.Y,
                    b.Y + TANK_SIZE - a.Y);

                if (overlapX < overlapY)
                {
                    float push = overlapX / 2f;

                    if (a.X < b.X)
                        TryPushX(a, b, -push, push);
                    else
                        TryPushX(a, b, push, -push);
                }
                else
                {
                    float push = overlapY / 2f;

                    if (a.Y < b.Y)
                        TryPushY(a, b, -push, push);
                    else
                        TryPushY(a, b, push, -push);
                }
            }
    }
    static void TryPushY(Tank a, Tank b, float da, float db)
    {
        bool aOK = CanPushTank(a, b, a.X, a.Y + da);
        bool bOK = CanPushTank(b, a, b.X, b.Y + db);
        //bool aOK = true;
        //bool bOK = true;
        if (aOK && bOK)
        {
            a.Y += da;
            b.Y += db;
        }
        else if (aOK)
        {
            a.Y += da;
        }
        else if (bOK)
        {
            b.Y += db;
        }
    }

    static void TryPushX(Tank a, Tank b, float da, float db)
    {
        bool aOK = CanPushTank(a, b, a.X + da, a.Y);
        bool bOK = CanPushTank(b, a, b.X + db, b.Y);
        //bool aOK = true;
        //bool bOK = true;
        if (aOK && bOK)
        {
            a.X += da;
            b.X += db;
        }
        else if (aOK)
        {
            a.X += da;
        }
        else if (bOK)
        {
            b.X += db;
        }
    }




    static void Broadcast()
    {
        StringBuilder sb = new();
        sb.Append("{");

        // ================= TANKS =================
        sb.Append("\"tanks\":[");
        bool ft = true;
        foreach (var t in clientTanks.Values)
        {
            if (!ft) sb.Append(",");
            sb.Append($"{{\"id\":{t.Id},\"Name\":\"{t.Name}\",\"x\":{t.X:F1},\"y\":{t.Y:F1},\"hp\":{t.HP},\"dirX\":{t.DirX},\"dirY\":{t.DirY},\"score\":{scores.GetValueOrDefault(t.Id)}}}");
            ft = false;
        }
        sb.Append("],");

        // ================= BULLETS =================
        sb.Append("\"bullets\":[");
        bool fb = true;
        foreach (var b in bullets)
        {
            if (!fb) sb.Append(",");
            sb.Append($"{{\"id\":{b.Id},\"x\":{b.X:F1},\"y\":{b.Y:F1},\"dirX\":{b.DirX},\"dirY\":{b.DirY}}}");
            fb = false;
        }
        sb.Append("],");

        // ================= HEALTHS =================
        sb.Append("\"healths\":[");
        bool fh = true;
        foreach (var h in healths)
        {
            if (!fh) sb.Append(",");
            sb.Append($"{{\"id\":{h.Id},\"x\":{h.X:F1},\"y\":{h.Y:F1}}}");
            fh = false;
        }
        sb.Append("]");

        sb.Append("}\n");

        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        foreach (var c in clients)
            try { c.GetStream().Write(data); } catch { }
    }

}
