using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib
{
    public class PdfDictionary
    {
        private Dictionary<string, object> dic = new Dictionary<string, object>();

        public PdfDictionary(PdfParser parser)
        {
            var lexer = parser.Lexer;
            lexer.ReadToken();
            while (lexer.Current != null)
            {
                if (lexer.Current == ">>")
                {
                    lexer.ReadToken();
                    break;
                }
                var key = lexer.Current;
                lexer.ReadToken();
                dic[key] = parser.Read();
            }
        }

        public object this[string key]
        {
            get { return dic.ContainsKey(key) ? dic[key] : null; }
        }

        public Dictionary<string, object>.KeyCollection Keys
        {
            get { return dic.Keys; }
        }

        public bool ContainsKey(string key)
        {
            return dic.ContainsKey(key);
        }
    }
}
