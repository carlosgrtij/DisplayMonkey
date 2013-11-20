﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Data;

namespace DisplayMonkey
{
	public class Panel
	{
		public Panel()
		{
		}

		public Panel(int panelId)
		{
			PanelId = panelId;

			string sql = string.Format(
				"SELECT TOP 1 * FROM PANEL WHERE PanelId={0};",
				panelId
				);

			using (DataSet ds = DataAccess.RunSql(sql))
			{
				if (ds.Tables[0].Rows.Count > 0)
				{
					DataRow r = ds.Tables[0].Rows[0];
					InitFromRow(r);
				}
			}
		}

		public void InitFromRow(DataRow r)
		{
			PanelId = DataAccess.IntOrZero(r["PanelId"]);
			Top = DataAccess.IntOrZero(r["Top"]);
			Left = DataAccess.IntOrZero(r["Left"]);
			Width = DataAccess.IntOrZero(r["Width"]);
			Height = DataAccess.IntOrZero(r["Height"]);
			Name = DataAccess.StringOrBlank(r["Name"]);
			if (Name == "")
				Name = string.Format("Panel {0}", PanelId);
		}

		public static List<Panel> List(int canvasId)
		{
			List<Panel> list = new List<Panel>();
			string sql = string.Format(
				"SELECT * FROM PANEL WHERE CanvasId={0} ORDER BY 1;" +
				"SELECT TOP 1 c.*, PanelId FROM FULL_SCREEN s INNER JOIN CANVAS c ON c.CanvasId=s.CanvasId WHERE s.CanvasId={0};",
				canvasId
				);
			using (DataSet ds = DataAccess.RunSql(sql))
			{
				list.Capacity = ds.Tables[0].Rows.Count;

				DataRow fs = ds.Tables[1].Rows[0];
				int fullScreenPanelId = DataAccess.IntOrZero(fs["PanelId"]);

				// list canvas panels
				foreach (DataRow r in ds.Tables[0].Rows)
				{
					Panel panel = null;
					int panelId = DataAccess.IntOrZero(r["PanelId"]);

					if (panelId == fullScreenPanelId)
						panel = new FullScreenPanel()
						{
							PanelId = panelId,
							Top = 0,
							Left = 0,
							Height = DataAccess.IntOrZero(fs["Height"]),
							Width = DataAccess.IntOrZero(fs["Width"]),
							Name = DataAccess.StringOrBlank(r["Name"]),
						};
					else
						panel = new Panel()
						{
							PanelId = panelId,
							Top = DataAccess.IntOrZero(r["Top"]),
							Left = DataAccess.IntOrZero(r["Left"]),
							Height = DataAccess.IntOrZero(r["Height"]),
							Width = DataAccess.IntOrZero(r["Width"]),
							Name = DataAccess.StringOrBlank(r["Name"]),
						};

					if (panel.Name == "")
						panel.Name = string.Format("Panel {0}", panelId);

					list.Add(panel);
				}
			}
			return list;
		}

		public int PanelId = 0;
		public int Top = 0;
		public int Left = 0;
		public int Width = 0;
		public int Height = 0;
		public string Name = "";

		public string BorderColor = "";

		public virtual string Style 
		{ 
			get 
			{ 
				return string.Format(
					"#div{0} {{position:absolute;overflow:hidden;margin:auto;top:{1}px;left:{2}px;width:{3}px;height:{4}px;{5}}}\r\n",
					new object[] {
						PanelId, 
						Top, 
						Left, 
						Width, 
						Height,
						BorderColor == "" ? "" : string.Format("border:1px solid {0};", BorderColor), 
					}); 
			} 
		}

		public virtual string Element
		{
			get
			{
				return string.Format(
					"<div id=\"div{0}\" data-panel-id=\"{0}\"></div><div id=\"h_div{0}\" style=\"display:none\"></div>\r\n", 
					PanelId
					);
			}
		}
	}
}