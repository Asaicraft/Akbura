using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Pools;
partial class ArrayBuilder<T>
{
    /// <summary>
    /// struct enumerator used in foreach.
    /// </summary>
    public struct Enumerator(ArrayBuilder<T> builder)
    {
        private readonly ArrayBuilder<T> _builder = builder;
        private int _index = -1;

        public readonly T Current => _builder[_index];

        public bool MoveNext()
        {
            _index++;
            return _index < _builder.Count;
        }
    }
}