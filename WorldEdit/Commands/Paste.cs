using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using TShockAPI;
using WorldEdit.Expressions;

namespace WorldEdit.Commands
{
	public class Paste : WECommand
	{
		private readonly int alignment;
		private readonly Expression expression;

		public Paste(int x, int y, TSPlayer plr, int alignment, Expression expression)
			: base(x, y, int.MaxValue, int.MaxValue, plr)
		{
			this.alignment = alignment;
			this.expression = expression;
		}

		public override void Execute()
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
	}
}