using System;
using System.Collections.Generic;
using System.Text;

namespace HeavyDuck.Eve
{
    public class EveApiException : Exception
    {
        private int m_code;

        public EveApiException(int code, string message)
            : base(message)
        {
            m_code = code;
        }

        public int ErrorCode
        {
            get { return m_code; }
        }

        public override string ToString()
        {
            return "(" + m_code.ToString() + ") " + base.ToString();
        }
    }
}
