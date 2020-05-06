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

		private readonly BlockingCollection<WECommand> _commandQueue = new BlockingCollection<WECommand>();

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

        }

        public void Paste()
        {
            
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

        }
	}
}
