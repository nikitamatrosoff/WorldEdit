using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using OTAPI.Tile;
using Terraria;
using TShockAPI;
using Terraria.GameContent.Tile_Entities;
using Terraria.DataStructures;
using Terraria.ID;
using System.Linq;

namespace WorldEdit
{
	public static class Tools
	{
		internal const int BUFFER_SIZE = 1048576;

		public static string GetClipboardPath(int accountID)
		{
			return Path.Combine(WorldEdit.WorldEditFolderName, string.Format("clipboard-{0}.dat", accountID));
		}

		#region LoadWorldSectionData

		public static WorldSectionData LoadWorldData(string path)
		{
			using (var reader =
				new BinaryReader(
					new BufferedStream(
						new GZipStream(File.Open(path, FileMode.Open), CompressionMode.Decompress), BUFFER_SIZE)))
			{
				var x = reader.ReadInt32();
				var y = reader.ReadInt32();
				var width = reader.ReadInt32();
				var height = reader.ReadInt32();
				var worldData = new WorldSectionData(width, height) { X = x, Y = y };

				for (var i = 0; i < width; i++)
				{
					for (var j = 0; j < height; j++)
						worldData.Tiles[i, j] = reader.ReadTile();
				}

				try
				{
					var signCount = reader.ReadInt32();
					worldData.Signs = new WorldSectionData.SignData[signCount];
					for (var i = 0; i < signCount; i++)
					{
						worldData.Signs[i] = reader.ReadSign();
					}

					var chestCount = reader.ReadInt32();
					worldData.Chests = new WorldSectionData.ChestData[chestCount];
					for (var i = 0; i < chestCount; i++)
					{
						worldData.Chests[i] = reader.ReadChest();
					}

					var itemFrameCount = reader.ReadInt32();
					worldData.ItemFrames = new WorldSectionData.ItemFrameData[itemFrameCount];
					for (var i = 0; i < itemFrameCount; i++)
					{
						worldData.ItemFrames[i] = reader.ReadItemFrame();
					}

					return worldData;
				}
				catch (EndOfStreamException) // old version file
				{
					return worldData;
				}
			}
		}

		private static Tile ReadTile(this BinaryReader reader)
		{
			var tile = new Tile
			{
				sTileHeader = reader.ReadInt16(),
				bTileHeader = reader.ReadByte(),
				bTileHeader2 = reader.ReadByte()
			};

			// Tile type
			if (tile.active())
			{
				tile.type = reader.ReadUInt16();
				if (Main.tileFrameImportant[tile.type])
				{
					tile.frameX = reader.ReadInt16();
					tile.frameY = reader.ReadInt16();
				}
			}
			tile.wall = reader.ReadByte();
			tile.liquid = reader.ReadByte();
			return tile;
		}

		private static WorldSectionData.SignData ReadSign(this BinaryReader reader)
		{
			return new WorldSectionData.SignData
			{
				X = reader.ReadInt32(),
				Y = reader.ReadInt32(),
				Text = reader.ReadString()
			};
		}

		private static WorldSectionData.ChestData ReadChest(this BinaryReader reader)
		{
			var x = reader.ReadInt32();
			var y = reader.ReadInt32();

			var count = reader.ReadInt32();
			var items = new NetItem[count];

			for (var i = 0; i < count; i++)
			{
				items[i] = new NetItem(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());
			}

			return new WorldSectionData.ChestData
			{
				Items = items,
				X = x,
				Y = y
			};
		}

		private static WorldSectionData.ItemFrameData ReadItemFrame(this BinaryReader reader)
		{
			return new WorldSectionData.ItemFrameData
			{
				X = reader.ReadInt32(),
				Y = reader.ReadInt32(),
				Item = new NetItem(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte())
			};
		}

		#endregion

		public static void LoadWorldSection(string path)
		{
			var data = LoadWorldData(path);

			for (var i = 0; i < data.Width; i++)
			{
				for (var j = 0; j < data.Height; j++)
				{
					Main.tile[i + data.X, j + data.Y] = data.Tiles[i, j];
					Main.tile[i + data.X, j + data.Y].skipLiquid(true);
				}
			}

			foreach (var sign in data.Signs)
			{
				var id = Sign.ReadSign(sign.X + data.X, sign.Y + data.Y);
				if (id == -1)
				{
					continue;
				}

				Sign.TextSign(id, sign.Text);
			}

			foreach (var itemFrame in data.ItemFrames)
			{
				var x = itemFrame.X + data.X;
				var y = itemFrame.Y + data.Y;

				var id = TEItemFrame.Place(x, y);
				if (id == -1)
				{
					continue;
				}

				WorldGen.PlaceObject(x, y, TileID.ItemFrame);
				var frame = (TEItemFrame) TileEntity.ByID[id];

				frame.item = new Item();
				frame.item.netDefaults(itemFrame.Item.NetId);
				frame.item.stack = itemFrame.Item.Stack;
				frame.item.prefix = itemFrame.Item.PrefixId;
			}

			foreach (var chest in data.Chests)
			{
				int chestX = chest.X + data.X, chestY = chest.Y + data.Y;

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

			ResetSection(data.X, data.Y, data.X + data.Width, data.Y + data.Height);
		}

		public static void SaveWorldSection(int x, int y, int x2, int y2, string path)
		{
			using (var writer =
				new BinaryWriter(
					new BufferedStream(
						new GZipStream(File.Open(path, FileMode.Create), CompressionMode.Compress), BUFFER_SIZE)))
			{
				var data = SaveWorldSection(x, y, x2, y2);

				data.Write(writer);
			}
		}

		public static void Write(this BinaryWriter writer, ITile tile)
		{
			writer.Write(tile.sTileHeader);
			writer.Write(tile.bTileHeader);
			writer.Write(tile.bTileHeader2);

			if (tile.active())
			{
				writer.Write(tile.type);
				if (Main.tileFrameImportant[tile.type])
				{
					writer.Write(tile.frameX);
					writer.Write(tile.frameY);
				}
			}
			writer.Write(tile.wall);
			writer.Write(tile.liquid);
		}

		public static WorldSectionData SaveWorldSection(int x, int y, int x2, int y2)
		{
			var width = x2 - x + 1;
			var height = y2 - y + 1;

			var data = new WorldSectionData(width, height)
			{
				X = x,
				Y = y,
				Chests = new List<WorldSectionData.ChestData>(),
				Signs = new List<WorldSectionData.SignData>(),
				ItemFrames = new List<WorldSectionData.ItemFrameData>()
			};

			for (var i = x; i <= x2; i++)
			{
				for (var j = y; j <= y2; j++)
				{
					data.ProcessTile(Main.tile[i, j], i - x, j - y);
				}
			}

			return data;
		}

        public static void ResetSection(int x1, int y1, int x2, int y2)
        {
            int lowX = Netplay.GetSectionX(x1);
            int highX = Netplay.GetSectionX(x2);
            int lowY = Netplay.GetSectionY(y1);
            int highY = Netplay.GetSectionY(y2);
            foreach (RemoteClient sock in Netplay.Clients.Where(s => s.IsActive))
                for (int i = lowX; i <= highX; i++)
                    for (int j = lowY; j <= highY; j++)
                        sock.TileSections[i, j] = false;
        }
    }
}