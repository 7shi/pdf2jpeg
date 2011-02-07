using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib
{
    public class PdfReference
    {
        public int Number { get; private set; }
        public int Index { get; private set; }

        public PdfReference(int num, int num2)
        {
            Number = num;
            Index = num2;
        }
    }
}
