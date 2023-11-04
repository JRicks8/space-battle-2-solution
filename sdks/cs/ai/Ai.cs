using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Transactions;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace ai
{
    public class Ai
    {
        private Random rand = new Random();
        private List<Unit> myUnits = new List<Unit>();
        private Tile[,] map;
        
        private List<Tile> resourceTiles = new List<Tile>();
        private int mapWidth;
        private int mapHeight;

        private int baseX, baseY;

        // timers
        private int searchMapTimer = 5;

        const int STATE_SEARCHING = 0;
        const int STATE_GATHERING = 1;
        const int ALL_IN = 2;
        private int state = STATE_SEARCHING;

        // search location
        int currentSearchLocation = 0; // 0 = N, 1 = NE, 2 = E & so on

        public Ai()
        {
        }

        public string GoJson(string line)
        {
            var gameMessage = JsonConvert.DeserializeObject<GameMessage>(line);

            var commandSet = Go(gameMessage);

            return JsonConvert.SerializeObject(commandSet) + "\n";
        }

        public CommandSet Go(GameMessage gameMessage)
        {
            Console.WriteLine("Got here!");
            searchMapTimer--;

            if (gameMessage.Turn == 0)
            {
                Console.WriteLine("FIRST TURN");
                mapWidth = gameMessage.Game_Info.Map_Width * 2 + 1;
                mapHeight = gameMessage.Game_Info.Map_Height * 2 + 1;
                baseX = mapWidth / 2;
                baseY = mapHeight / 2;
                map = new Tile[mapWidth, mapHeight];
                for (int i = 0; i < map.GetLength(0); i++)
                {
                    for (int j = 0; j < map.GetLength(1); j++)
                    {
                        map[i, j] = new Tile();
                    }
                }
            }

            foreach (var tile in gameMessage.Tile_Updates) 
            {
                tile.X += mapWidth / 2;
                tile.Y += mapHeight / 2;
                map[tile.X, tile.Y] = new Tile()
                {
                    Visible = tile.Visible,
                    X = tile.X,
                    Y = tile.Y,
                    Blocked = tile.Blocked,
                    Resources = tile.Resources
                };
            }

            foreach (var unitUpdate in gameMessage.Unit_Updates)
            {
                bool alreadyPresent = false;
                int index = -1;
                for (int i = 0; i < myUnits.Count; i++)
                {
                    if (myUnits[i].unit_id == unitUpdate.Id)
                    {
                        index = i;
                        alreadyPresent = true;
                        break;
                    }
                }

                if (alreadyPresent)
                {
                    myUnits[index].isMyUnit = true;
                    myUnits[index].unit_id = unitUpdate.Id;
                    myUnits[index].player_id = unitUpdate.Player_Id;
                    myUnits[index].x = unitUpdate.X;
                    myUnits[index].y = unitUpdate.Y;
                    myUnits[index].status = unitUpdate.Status;
                    myUnits[index].type = unitUpdate.Type;
                    myUnits[index].resource = unitUpdate.Resource;
                    myUnits[index].health = unitUpdate.Health;
                    myUnits[index].can_attack = unitUpdate.Can_Attack;
                }                  
                else
                {
                    myUnits.Add(new Unit()
                    {
                        isMyUnit = true,
                        unit_id = unitUpdate.Id,
                        player_id = unitUpdate.Player_Id,
                        x = unitUpdate.X,
                        y = unitUpdate.Y,
                        status = unitUpdate.Status,
                        type = unitUpdate.Type,
                        resource = unitUpdate.Resource,
                        health = unitUpdate.Health,
                        can_attack = unitUpdate.Can_Attack
                    });
                }
            }

            var gameCommands = myUnits.Select(GetUnitCommand);
            gameCommands.Append(new GameCommand()
            {
                command = "CREATE",
                type = "worker"
            });

            return new CommandSet { commands = gameCommands };
        }

        private GameCommand GetUnitCommand(Unit unit)
        {
            GameCommand cmd;

            Console.WriteLine("Generating Command for unit " + unit.unit_id + " type " + unit.type);
            Console.WriteLine(unit.type);

            if (unit.type == "worker") cmd = GetWorkerCommand(unit);
            else return new GameCommand()
            {
                command = "MOVE",
                unit = unit.unit_id,
                dir = "NESW".Substring(rand.Next(4), 1)
            };

            return cmd;
        }

        private GameCommand GetWorkerCommand(Unit unit)
        {
            bool stuckThisTurn = false;
            if (unit.x == unit.lastX && unit.y == unit.lastY)
            {
                stuckThisTurn = true;
                unit.numTurnsStuck++;
                if (unit.numTurnsStuck > 4) return new GameCommand()
                {
                    command = "MOVE",
                    unit = unit.unit_id,
                    dir = "NESW".Substring(rand.Next(4), 1)
                };
            }
            unit.lastX = unit.x;
            unit.lastY = unit.y;
           
            if (unit.resource > 0)
            {
                int dx, dy;
                string direction;
                dx = baseX - (unit.x + mapWidth / 2);
                dy = baseY - (unit.y + mapWidth / 2);
                Console.WriteLine("moving from " + (unit.x + mapWidth / 2) + 
                    " " + (unit.y + mapHeight / 2));
                Console.WriteLine("to " + baseX + " " + baseY);
                Console.WriteLine("dx " + dx + " dy " + dy);
                if (stuckThisTurn)
                {
                    if (dy > 0) direction = "S";
                    else if (dy < 0) direction = "N";
                    else if (dx > 0) direction = "E";
                    else direction = "W";
                }
                else
                {
                    if (dx > 0) direction = "E";
                    else if (dx < 0) direction = "W";
                    else if (dy > 0) direction = "S";
                    else direction = "N";
                }
                return new GameCommand()
                {
                    command = "MOVE",
                    unit = unit.unit_id,
                    dir = direction
                };
            }
            else
            {
                List<Tile> neighbors = new List<Tile>();
                neighbors.Add(map[unit.x + mapWidth / 2 + 1, unit.y + mapHeight / 2]);
                neighbors.Add(map[unit.x + mapWidth / 2, unit.y + mapHeight / 2 + 1]);
                neighbors.Add(map[unit.x + mapWidth / 2 - 1, unit.y + mapHeight / 2]);
                neighbors.Add(map[unit.x + mapWidth / 2, unit.y + mapHeight / 2 - 1]);

                for (int i = 0; i < neighbors.Count; i++)
                {
                    if (neighbors[i] != null)
                    {
                        if (neighbors[i].Resources != null)
                        {
                            string direction;
                            if (i == 0) direction = "E";
                            else if (i == 1) direction = "S";
                            else if (i == 2) direction = "W";
                            else direction = "N";
                            return new GameCommand()
                            {
                                command = "GATHER",
                                unit = unit.unit_id,
                                dir = direction
                            };
                        }
                    }
                }
            }
            return new GameCommand()
            {
                command = "MOVE",
                unit = unit.unit_id,
                dir = "NESW".Substring(rand.Next(4), 1)
            };
        }

        //private GameCommand GetWorkerCommand(Unit unit)
        //{
        //    Console.WriteLine("Generating Worker Command for unit " + unit.unit_id);
        //    if (state == STATE_GATHERING)
        //    {
        //        Console.WriteLine("State is Gathering");
        //        if (searchMapTimer <= 0)
        //        {
        //            Console.WriteLine("Searching for resources");
        //            resourceTiles = SearchMapForResources();
        //            searchMapTimer = 10;
        //            if (resourceTiles.Count == 0) state = STATE_SEARCHING;
        //        }
        //        if (unit.currentPath == null) // if the unit currently doesn't have a path
        //        {
        //            Console.WriteLine("Unit does not have a path currently");
        //            if (resourceTiles.Count > 0) // if there are resources to be collected
        //            {
        //                Console.WriteLine("There are resources");
        //                Tile desiredResourceTile = resourceTiles[0];
        //                Console.WriteLine("Trying to path to " + desiredResourceTile.X + " " + desiredResourceTile.Y);
        //                Console.WriteLine("From " + (unit.x + mapWidth / 2) + " " + (unit.y + mapHeight / 2));
        //                List<Tile> pathToTile = AStar.FindPath(map, unit.x + mapWidth / 2, unit.y + mapHeight / 2, desiredResourceTile.X, desiredResourceTile.Y);
        //                if (pathToTile != null)
        //                {
        //                    unit.currentPath = pathToTile;
        //                    return unit.MoveAlongPath();
        //                }
        //            }
        //            else if (unit.resource > 0) // if we already have resources
        //            {
        //                Console.WriteLine("Unit has a resource");
        //                // move to base
        //            }
        //        }
        //        else // does have path
        //        {
        //            return unit.MoveAlongPath();
        //        }
        //    }
        //    else if (state == STATE_SEARCHING)
        //    {
        //        Console.WriteLine("State is Searching");
        //        if (searchMapTimer <= 0)
        //        {
        //            Console.WriteLine("Searching for resources");
        //            resourceTiles = SearchMapForResources();
        //            searchMapTimer = 5;
        //            if (resourceTiles.Count > 0)
        //            {
        //                state = STATE_GATHERING;
        //                Console.WriteLine("Found resources!");
        //            }
        //        }
        //        if (unit.currentPath == null)
        //        {
        //            Console.WriteLine("No Path currently following");
        //            if (unit.resource > 0) // if we already have resources
        //            {
        //                Console.WriteLine("We have resources");
        //                unit.currentPath = AStar.FindPath(map, unit.x, unit.y, mapWidth / 2, mapHeight / 2);
        //            } 
        //            else // pick a destination to move to
        //            {
        //                Console.WriteLine("Moving to some other destination");
        //                int destX, destY;
        //                if (currentSearchLocation == 0)
        //                {
        //                    destX = mapWidth / 2;
        //                    destY = 0;
        //                }
        //                else if (currentSearchLocation == 1)
        //                {
        //                    destX = mapWidth - 1;
        //                    destY = mapHeight / 2;
        //                }
        //                else if (currentSearchLocation == 2)
        //                {
        //                    destX = mapWidth / 2;
        //                    destY = mapHeight - 1;
        //                }
        //                else if (currentSearchLocation == 3)
        //                {
        //                    destX = 0;
        //                    destY = mapHeight / 2;
        //                }
        //                unit.currentPath = AStar.FindPath(map, unit.x, unit.y, mapWidth / 2, mapHeight / 2);
        //                currentSearchLocation++;
        //            }
        //        }
        //        else
        //        {
        //            Console.WriteLine("Moving along path");
        //            return unit.MoveAlongPath();
        //        }
        //    }
        //    return new GameCommand()
        //    {
        //        command = "MOVE",
        //        unit = unit.unit_id,
        //        dir = "NESW".Substring(rand.Next(4), 1)
        //    };
        //}

        private List<Tile> SearchMapForResources()
        {
            List<Tile> tiles = new List<Tile>();
            foreach (Tile tile in map)
            {
                if (tile.Resources != null) tiles.Add(tile);
            }

            return tiles;
        }
    }
}

public class AStar
{
    public static List<Tile> FindPath(Tile[,] grid, int startX, int startY, int endX, int endY)
    {
        if (grid[endX, endY] == null) grid[endX, endY] = new Tile();

        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        // Check if the start and end points are valid
        if (startX < 0 || startX >= width || startY < 0 || startY >= height ||
            endX < 0 || endX >= width || endY < 0 || endY >= height ||
            grid[startX, startY].Blocked == true || grid[endX, endY].Blocked == true)
        {
            return null; // No valid path
        }

        List<Tile> openList = new List<Tile>();
        List<Tile> closedList = new List<Tile>();

        openList.Add(grid[startX, startY]);

        while (openList.Count > 0)
        {
            Tile currentPathfindTile = openList[0];

            // Find the node with the lowest F cost in the open list
            for (int i = 1; i < openList.Count; i++)
            {
                if (openList[i].F < currentPathfindTile.F || (openList[i].F == currentPathfindTile.F && openList[i].H < currentPathfindTile.H))
                {
                    currentPathfindTile = openList[i];
                }
            }

            openList.Remove(currentPathfindTile);
            closedList.Add(currentPathfindTile);

            if (currentPathfindTile.X == endX && currentPathfindTile.Y == endY)
            {
                // Path found, reconstruct and return it
                List<Tile> path = new List<Tile>();
                while (currentPathfindTile != null)
                {
                    path.Add(currentPathfindTile);
                    currentPathfindTile = currentPathfindTile.Parent;
                }
                path.Reverse();
                return path;
            }

            // Generate successors
            List<Tile> successors = new List<Tile>();

            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int x = currentPathfindTile.X + dx[i];
                int y = currentPathfindTile.Y + dy[i];

                if (x >= 0 && x < width && y >= 0 && y < height && grid[x, y].Blocked == false)
                {
                    grid[x,y].Parent = currentPathfindTile;
                    grid[x, y].G = currentPathfindTile.G + 1;
                    grid[x, y].H = Math.Abs(grid[x, y].X - endX) + Math.Abs(grid[x, y].Y - endY);

                    // Check if the neighbor is already in the closed list
                    if (closedList.Contains(grid[x, y]))
                    {
                        continue;
                    }

                    // Check if the neighbor is already in the open list
                    int existingIndex = openList.FindIndex(n => n.X == grid[x, y].X && n.Y == grid[x, y].Y);
                    if (existingIndex != -1)
                    {
                        Tile existingPathfindTile = openList[existingIndex];
                        if (grid[x, y].G < existingPathfindTile.G)
                        {
                            existingPathfindTile.G = grid[x, y].G;
                            existingPathfindTile.Parent = grid[x, y].Parent;
                        }
                    }
                    else
                    {
                        openList.Add(grid[x, y]);
                    }
                }
            }
        }

        return null; // No path found
    }
}

public class GameMessage
{
    public int Player { get; set; }
    public int Turn { get; set; }
    public int Time { get; set; }
    public IList<UnitUpdate> Unit_Updates { get; set; }
    public IList<TileUpdate> Tile_Updates { get; set; }
    public GameInfo Game_Info { get; set; }
}

public class GameInfo
{
    public int Map_Width { get; set; }
    public int Map_Height  { get; set; }
    public int Game_Duration  { get; set; }
    public int Turn_Duration  { get; set; }
}

public class Unit
{
    public bool isMyUnit { get; set; }

    public int unit_id { get; set; }
    public int player_id  { get; set; }
    public int x  { get; set; }
    public int y  { get; set; }
    public string status  { get; set; }
    public string type  { get; set; }
    public int resource  { get; set; }
    public int health  { get; set; }
    public bool can_attack  { get; set; }

    public List<Tile> currentPath;

    public int lastX;
    public int lastY;
    public int numTurnsStuck = 0;
}

static class Worker
{
    public static readonly string name = "WORKER";
    public static readonly int cost = 100;
    public static readonly int range = 2;
    public static readonly int speed = 5;
    public static readonly int maxHealth = 10;
    public static readonly int attackCooldown = 3;
    public static readonly int attackDamage = 2;
    public static readonly int buildTime = 5;
}

public class Tile
{
    public bool Visible { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Blocked { get; set; }
    public Resource Resources { get; set; }
    public Unit EnemyUnits { get; set; }

    // pathfinding
    public int G;
    public int H;
    public Tile Parent;

    public int F => G + H; // Total cost (F = G + H)

    public Tile()
    {
        Blocked = false;
        G = 0;
        H = 0;
        Parent = null;
    }
}

public class UnitUpdate
{
    public int Id { get; set; }
    public int Player_Id { get; set; }
    public string Status { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Type { get; set; }
    public int Resource { get; set; }
    public int Health { get; set; }
    public bool Can_Attack { get; set; }
}

public class TileUpdate
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool Visible { get; set; }
    public bool Blocked { get; set; }
    public Resource Resources { get; set; }
}

public class Resource
{
    public int Id { get; set; }
    public string Type { get; set; }
    public int Total { get; set; }
    public int Value { get; set; }
}

public class CommandSet
{
    public IEnumerable<GameCommand> commands;
}

public class GameCommand
{
    public string command { get; set; }
    public int unit { get; set; }
    public string dir { get; set; }
    public string type { get; set; }
}