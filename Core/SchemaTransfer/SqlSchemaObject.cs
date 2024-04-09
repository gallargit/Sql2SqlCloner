﻿using Microsoft.SqlServer.Management.Smo;
using System.Drawing;
using System.Linq;

namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class SqlSchemaObject
    {
        public Bitmap Status { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool CopyData { get; set; }
        public NamedSmoObject Object { get; set; }
        public string Error { get; set; }
        [System.ComponentModel.Browsable(false)]
        public SqlSchemaObject Parent { get; set; }
        [System.ComponentModel.Browsable(false)]
        public string NameWithBrackets
        {
            get
            {
                var itemWithBrackets = "";
                Name.Split('.').ToList().ForEach(itemSplitDot =>
                    itemWithBrackets += ((itemWithBrackets != "") ? "." : "") +
                        (itemSplitDot.StartsWith("[") ? "" : "[") +
                        itemSplitDot +
                        (itemSplitDot.EndsWith("]") ? "" : "]"));

                return itemWithBrackets;
            }
        }
        [System.ComponentModel.Browsable(false)]
        public string NameWithoutBrackets
        {
            get
            {
                return Name.Replace("[", "").Replace("]", "");
            }
        }
    }
}
