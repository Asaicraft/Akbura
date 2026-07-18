using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura;


public class AkburaException : Exception
{
	public AkburaException() { }
	public AkburaException(string message) : base(message) { }
	public AkburaException(string message, Exception inner) : base(message, inner) { }
}
