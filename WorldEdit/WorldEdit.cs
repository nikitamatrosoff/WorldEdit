using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using WorldEdit.Commands;

namespace WorldEdit
{
	public delegate bool Selection(int i, int j, TSPlayer player);

	public class WorldEdit
	{
		public const string WorldEditFolderName = "worldedit";

		public static Dictionary<string, int[]> Biomes = new Dictionary<string, int[]>();
		public static Dictionary<string, int> Colors = new Dictionary<string, int>();
		public static Dictionary<string, Selection> Selections = new Dictionary<string, Selection>();
		public static Dictionary<string, int> Tiles = new Dictionary<string, int>();
		public static Dictionary<string, int> Walls = new Dictionary<string, int>();
		public static Dictionary<string, int> Slopes = new Dictionary<string, int>();

		private void OnInitialize(EventArgs e)
		{
            if (!Directory.Exists(WorldEditFolderName))
                Directory.CreateDirectory(WorldEditFolderName);

			#region Colors
			Colors.Add("blank", 0);

			Main.player[Main.myPlayer] = new Player();
			var item = new Item();
			for (var i = 1; i < Main.maxItemTypes; i++)
			{
				item.netDefaults(i);

				if (item.paint <= 0)
				{
					continue;
				}

				var name = TShockAPI.Localization.EnglishLanguage.GetItemNameById(i);
				Colors.Add(name.Substring(0, name.Length - 6).ToLowerInvariant(), item.paint);
			}
			#endregion
			#region Selections
			Selections.Add("altcheckers", (i, j, plr) => ((i + j) & 1) == 0);
			Selections.Add("checkers", (i, j, plr) => ((i + j) & 1) == 1);
			Selections.Add("normal", (i, j, plr) => true);
			Selections.Add("border", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();
				return i == info.X || i == info.X2 || j == info.Y || j == info.Y2;
			});
			Selections.Add("outline", (i, j, plr) =>
			{
				return ((i > 0) && (j > 0) && (i < Main.maxTilesX - 1) && (j < Main.maxTilesY - 1)
					&& (Main.tile[i, j].active())
					&& ((!Main.tile[i - 1, j].active()) || (!Main.tile[i, j - 1].active())
					|| (!Main.tile[i + 1, j].active()) || (!Main.tile[i, j + 1].active())
					|| (!Main.tile[i + 1, j + 1].active()) || (!Main.tile[i - 1, j - 1].active())
					|| (!Main.tile[i - 1, j + 1].active()) || (!Main.tile[i + 1, j - 1].active())));
			});
			Selections.Add("45", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();

				int X = Math.Min(info.X, info.X2);
				int Y = Math.Min(info.Y, info.Y2);

				return (i - X) == (j - Y);
			});
			Selections.Add("225", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();

				int Y = Math.Min(info.Y, info.Y2);
				int X2 = Math.Max(info.X, info.X2);

				return (X2 - i) == (j - Y);
			});
			#endregion
			#region Tiles
			Tiles.Add("air", -1);
			Tiles.Add("lava", -2);
			Tiles.Add("honey", -3);
			Tiles.Add("water", -4);

			foreach (var fi in typeof(TileID).GetFields())
			{
				string name = fi.Name;
				var sb = new StringBuilder();
				for (int i = 0; i < name.Length; i++)
				{
					if (char.IsUpper(name[i]))
						sb.Append(" ").Append(char.ToLower(name[i]));
					else
						sb.Append(name[i]);
				}
				Tiles.Add(sb.ToString(1, sb.Length - 1), (ushort)fi.GetValue(null));
			}
			#endregion
			#region Walls
			Walls.Add("air", 0);

			foreach (var fi in typeof(WallID).GetFields())
			{
				string name = fi.Name;
				var sb = new StringBuilder();
				for (int i = 0; i < name.Length; i++)
				{
					if (char.IsUpper(name[i]))
						sb.Append(" ").Append(char.ToLower(name[i]));
					else
						sb.Append(name[i]);
				}
				Walls.Add(sb.ToString(1, sb.Length - 1), (byte)fi.GetValue(null));
			}
			#endregion
		}

        public void Copy()
        {
            string clipboardPath = Tools.GetClipboardPath(plr.User.ID);

            Tools.SaveWorldSection(x, y, x2, y2, clipboardPath);
        }

        public void Paste()
        {
            var clipboardPath = Tools.GetClipboardPath(plr.User.ID);

            var data = Tools.LoadWorldData(clipboardPath);

            var width = data.Width - 1;
            var height = data.Height - 1;

            if ((alignment & 1) == 0)
                x2 = x + width;
            else
            {
                x2 = x;
                x -= width;
            }
            if ((alignment & 2) == 0)
                y2 = y + height;
            else
            {
                y2 = y;
                y -= height;
            }

            Tools.PrepareUndo(x, y, x2, y2, plr);

            for (var i = x; i <= x2; i++)
            {
                for (var j = y; j <= y2; j++)
                {
                    if (i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY ||
                        expression != null && !expression.Evaluate(Main.tile[i, j]))
                    {
                        continue;
                    }

                    var index1 = i - x;
                    var index2 = j - y;

                    Main.tile[i, j] = data.Tiles[index1, index2];
                }
            }

            foreach (var sign in data.Signs)
            {
                var id = Sign.ReadSign(sign.X + x, sign.Y + y);
                if (id == -1)
                {
                    continue;
                }

                Sign.TextSign(id, sign.Text);
            }

            foreach (var itemFrame in data.ItemFrames)
            {
                var ifX = itemFrame.X + x;
                var ifY = itemFrame.Y + y;

                var id = TEItemFrame.Place(ifX, ifY);
                if (id == -1)
                {
                    continue;
                }

                WorldGen.PlaceObject(ifX, ifY, TileID.ItemFrame);
                var frame = (TEItemFrame)TileEntity.ByID[id];

                frame.item = new Item();
                frame.item.netDefaults(itemFrame.Item.NetId);
                frame.item.stack = itemFrame.Item.Stack;
                frame.item.prefix = itemFrame.Item.PrefixId;
            }

            foreach (var chest in data.Chests)
            {
                int chestX = chest.X + x, chestY = chest.Y + y;

                int id;
                if ((id = Chest.FindChest(chestX, chestY)) == -1 &&
                    (id = Chest.CreateChest(chestX, chestY)) == -1)
                {
                    continue;
                }

                WorldGen.PlaceChest(chestX, chestY);
                for (var index = 0; index < chest.Items.Length; index++)
                {
                    var netItem = chest.Items[index];
                    var item = new Item();
                    item.netDefaults(netItem.NetId);
                    item.stack = netItem.Stack;
                    item.prefix = netItem.PrefixId;
                    Main.chest[id].item[index] = item;

                }
            }

            ResetSection();
            plr.SendSuccessMessage("Pasted clipboard to selection.");
        }

        public void Schematic()
        {
            const string fileFormat = "schematic-{0}.dat";
            if (e.Parameters.Count != 2)
            {
                e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //schematic load <name>");
                return;
            }

            var path = Path.Combine("worldedit", string.Format(fileFormat, e.Parameters[1]));

            var clipboard = Tools.GetClipboardPath(e.Player.User.ID);

            if (File.Exists(path))
            {
                File.Copy(path, clipboard, true);
            }
            else
            {
                e.Player.SendErrorMessage("Invalid schematic '{0}'!");
                return;
            }

            e.Player.SendSuccessMessage("Loaded schematic '{0}' to clipboard.", e.Parameters[1]);
        }

        public void Set()
        {
            Tools.PrepareUndo(x, y, x2, y2, plr);
            int edits = 0;
            for (int i = x; i <= x2; i++)
            {
                for (int j = y; j <= y2; j++)
                {
                    var tile = Main.tile[i, j];
                    if (((tileType >= 0 && (!tile.active() || tile.type != tileType)) ||
                        (tileType == -1 && tile.active()) ||
                        (tileType == -2 && (tile.liquid == 0 || tile.liquidType() != 1)) ||
                        (tileType == -3 && (tile.liquid == 0 || tile.liquidType() != 2)) ||
                        (tileType == -4 && (tile.liquid == 0 || tile.liquidType() != 0))) &&
                        select(i, j, plr) && expression.Evaluate(tile))
                    {
                        SetTile(i, j, tileType);
                        edits++;
                    }
                }
            }
            ResetSection();
            plr.SendSuccessMessage("Set tiles. ({0})", edits);
        }

        public void SetWall()
        {
            Tools.PrepareUndo(x, y, x2, y2, plr);
            int edits = 0;
            for (int i = x; i <= x2; i++)
            {
                for (int j = y; j <= y2; j++)
                {
                    var tile = Main.tile[i, j];
                    if (tile.wall != wallType && select(i, j, plr) && expression.Evaluate(tile))
                    {
                        tile.wall = (byte)wallType;
                        edits++;
                    }
                }
            }
            ResetSection();

            string wallName = wallType == 0 ? "air" : "wall " + wallType;
            plr.SendSuccessMessage("Set walls to {0}. ({1})", wallName, edits);
        }

        public void Cut()
        {
            for (int i = x; i <= x2; i++)
            {
                for (int j = y; j <= y2; j++)
                {
                    var tile = Main.tile[i, j];
                    switch (tile.type)
                    {
                        case Terraria.ID.TileID.Signs:
                        case Terraria.ID.TileID.Tombstones:
                        case Terraria.ID.TileID.AnnouncementBox:
                            if (tile.frameX % 36 == 0 && tile.frameY == 0)
                            {
                                Sign.KillSign(i, j);
                            }
                            break;
                        case Terraria.ID.TileID.Containers:
                        case Terraria.ID.TileID.Dressers:
                            if (tile.frameX % 36 == 0 && tile.frameY == 0)
                            {
                                Chest.DestroyChest(i, j);
                            }
                            break;
                        case Terraria.ID.TileID.ItemFrame:
                            if (tile.frameX % 36 == 0 && tile.frameY == 0)
                            {
                                Terraria.GameContent.Tile_Entities.TEItemFrame.Kill(i, j);
                            }
                            break;
                    }
                    Main.tile[i, j] = new Tile();
                }
            }

            ResetSection();
            plr.SendSuccessMessage("Cut selection. ({0})", (x2 - x + 1) * (y2 - y + 1));
        }

        public void Paint()
        {
            Tools.PrepareUndo(x, y, x2, y2, plr);
            int edits = 0;
            for (int i = x; i <= x2; i++)
            {
                for (int j = y; j <= y2; j++)
                {
                    var tile = Main.tile[i, j];
                    if (tile.active() && tile.color() != color && select(i, j, plr) && expression.Evaluate(tile))
                    {
                        tile.color((byte)color);
                        edits++;
                    }
                }
            }
            ResetSection();
            plr.SendSuccessMessage("Painted tiles. ({0})", edits);
        }

        public void PaintWall()
        {
            Tools.PrepareUndo(x, y, x2, y2, plr);
            int edits = 0;
            for (int i = x; i <= x2; i++)
            {
                for (int j = y; j <= y2; j++)
                {
                    var tile = Main.tile[i, j];
                    if (tile.wall > 0 && tile.wallColor() != color && select(i, j, plr) && expression.Evaluate(tile))
                    {
                        tile.wallColor((byte)color);
                        edits++;
                    }
                }
            }
            ResetSection();
            plr.SendSuccessMessage("Painted walls. ({0})", edits);
        }

        public void SetTile(int i, int j, int tileType)
        {
            var tile = Main.tile[i, j];
            switch (tileType)
            {
                case -1:
                    tile.active(false);
                    tile.frameX = -1;
                    tile.frameY = -1;
                    tile.liquidType(0);
                    tile.liquid = 0;
                    tile.type = 0;
                    return;
                case -2:
                    tile.active(false);
                    tile.liquidType(1);
                    tile.liquid = 255;
                    tile.type = 0;
                    return;
                case -3:
                    tile.active(false);
                    tile.liquidType(2);
                    tile.liquid = 255;
                    tile.type = 0;
                    return;
                case -4:
                    tile.active(false);
                    tile.liquidType(0);
                    tile.liquid = 255;
                    tile.type = 0;
                    return;
                default:
                    if (Main.tileFrameImportant[tileType])
                        WorldGen.PlaceTile(i, j, tileType);
                    else
                    {
                        tile.active(true);
                        tile.frameX = -1;
                        tile.frameY = -1;
                        tile.liquidType(0);
                        tile.liquid = 0;
                        tile.slope(0);
                        tile.type = (ushort)tileType;
                    }
                    return;
            }
        }

        public void ResetSection()
        {
            int lowX = Netplay.GetSectionX(x);
            int highX = Netplay.GetSectionX(x2);
            int lowY = Netplay.GetSectionY(y);
            int highY = Netplay.GetSectionY(y2);
            foreach (RemoteClient sock in Netplay.Clients.Where(s => s.IsActive))
            {
                for (int i = lowX; i <= highX; i++)
                {
                    for (int j = lowY; j <= highY; j++)
                        sock.TileSections[i, j] = false;
                }
            }
        }
    }
}
