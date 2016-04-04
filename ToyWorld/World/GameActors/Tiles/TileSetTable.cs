﻿using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace World.GameActors.Tiles
{
    public class TilesetTable
    {
        private readonly Dictionary<string, int> m_namesValuesDictionary;
        private readonly Dictionary<int, string> m_valuesNamesDictionary;

        public TilesetTable(StreamReader tilesetFile)
        {
            var dataTable = new DataTable();
            var readLine = tilesetFile.ReadLine();

            Debug.Assert(readLine != null, "readLine != null");
            foreach (var header in readLine.Split(';'))
            {
                dataTable.Columns.Add(header);
            }


            while (!tilesetFile.EndOfStream)
            {
                var line = tilesetFile.ReadLine();
                Debug.Assert(line != null, "line != null");
                var row = line.Split(';');
                if (dataTable.Columns.Count != row.Length)
                    break;
                var newRow = dataTable.NewRow();
                foreach (DataColumn column in dataTable.Columns)
                {
                    newRow[column.ColumnName] = row[column.Ordinal];
                }
                dataTable.Rows.Add(newRow);
            }

            var enumerable = dataTable.Rows.Cast<DataRow>();
            var dataRows = enumerable as DataRow[] ?? enumerable.ToArray();
            
            m_namesValuesDictionary = dataRows.Where(x => x["IsDefault"].ToString() == "1")
                .ToDictionary(x => x["NameOfTile"].ToString(), x => int.Parse(x["PositionInTileset"].ToString()));
            m_valuesNamesDictionary = dataRows.ToDictionary(x => int.Parse(x["PositionInTileset"].ToString()), x => x["NameOfTile"].ToString());
        }

        /// <summary>
        /// only for mocking
        /// </summary>
        public TilesetTable()
        {
            
        }

        public virtual int TileNumber(string tileName)
        {
            return m_namesValuesDictionary[tileName];
        }

        public virtual string TileName(int tileNumber)
        {
            return m_valuesNamesDictionary.ContainsKey(tileNumber) ? m_valuesNamesDictionary[tileNumber] : null;
        }
    }
}