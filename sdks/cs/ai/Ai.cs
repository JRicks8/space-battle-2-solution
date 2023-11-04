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
            if (unit.randomTurns > 0)
            {
                unit.randomTurns--;
                return new GameCommand()
                {
                    command = "MOVE",
                    unit = unit.unit_id,
                    dir = "NESW".Substring(rand.Next(4), 1)
                };
            }
            bool stuckThisTurn = false;
            if (unit.x == unit.lastX && unit.y == unit.lastY)
            {
                stuckThisTurn = true;
                unit.numTurnsStuck++;
                if (unit.numTurnsStuck > 4)
                {
                    unit.randomTurns = 5;
                    return new GameCommand()
                    {
                        command = "MOVE",
                        unit = unit.unit_id,
                        dir = "NESW".Substring(rand.Next(4), 1)
                    };
                }
            }
            unit.lastX = unit.x;
            unit.lastY = unit.y;
           
            if (unit.resource > 0)
            {
                map[unit.x + baseX, unit.y + baseY].pheromones++;
                if (unit.lastMoveDir == "N")
                    map[unit.x + baseX, unit.y + baseY].pheromoneDirection = "S";
                else if (unit.lastMoveDir == "S")
                    map[unit.x + baseX, unit.y + baseY].pheromoneDirection = "N";
                else if (unit.lastMoveDir == "E")
                    map[unit.x + baseX, unit.y + baseY].pheromoneDirection = "W";
                else 
                    map[unit.x + baseX, unit.y + baseY].pheromoneDirection = "E";

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
                unit.lastMoveDir = direction;
                return new GameCommand()
                {
                    command = "MOVE",
                    unit = unit.unit_id,
                    dir = direction
                };
            }
            else
            {
                map[unit.x + baseX, unit.y + baseY].pheromones = 0;
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
                        else if (neighbors[i].pheromones > 0)
                        {
                            string direction;
                            if (i == 0) direction = "E";
                            else if (i == 1) direction = "S";
                            else if (i == 2) direction = "W";
                            else direction = "N";
                            return new GameCommand()
                            {
                                command = "MOVE",
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
    public string lastMoveDir = "N";
    public int randomTurns = 0;
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

    public int pheromones = 0;
    public string pheromoneDirection = "N";
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