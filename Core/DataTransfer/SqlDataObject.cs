﻿using System.Drawing;

namespace Sql2SqlCloner.Core.DataTransfer
{
    public class SqlDataObject
    {
        public Bitmap Status { get; set; }
        public string Table { get; set; }
        public string SqlCommand { get; set; }
        public long TopRecords { get; set; }
        public string WhereFilter { get; set; }
        public long RowCount { get; set; }
        public bool HasRelationships { get; set; }
        public string Error { get; set; }
    }
}
