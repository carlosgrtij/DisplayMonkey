﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace DisplayMonkey
{
	public class FullScreenPanel : Panel
	{
		public FullScreenPanel()
			: base()
		{
		}

		public FullScreenPanel(int panelId)
            : base(panelId)
		{
            Top = Left = 0;
            
            string sql = string.Format(
				"SELECT TOP 1 c.* FROM FullScreen s INNER JOIN Canvas c ON c.CanvasId=s.CanvasId WHERE PanelId={0};",
			    PanelId
				);

			using (DataSet ds = DataAccess.RunSql(sql))
			{
				if (ds.Tables[0].Rows.Count > 0)
				{
					DataRow r = ds.Tables[0].Rows[0];
                    _initFromRow(r);
				}
			}
		}

		private void _initFromRow(DataRow r)
		{
			Width = r.IntOrZero("Width");
			Height = r.IntOrZero("Height");
		}

		public override string Style
		{
			get
			{
				StringBuilder style = new StringBuilder();

				style.AppendFormat(
                    "#full, #x_full {{overflow:hidden;width:{0}px;height:{1}px;}}\n",
					Width,
					Height
					);

				return style.ToString();
			}
		}
		
		public override string Element
		{
			get
			{
				return string.Format(
                    "<div id=\"screen\"><div class=\"fullpanel\" id=\"full\" data-panel-id=\"{0}\" data-panel-width=\"{1}\" data-panel-height=\"{2}\"></div></div>\n",
                    PanelId,
                    Width,
                    Height
					);
			}
		}
	}
}