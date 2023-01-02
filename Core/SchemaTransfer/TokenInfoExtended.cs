using Babel;

namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class TokenInfoExtended : TokenInfo
    {
        public string SQL { get; set; }
        public string Separators { get; set; }
    }
}