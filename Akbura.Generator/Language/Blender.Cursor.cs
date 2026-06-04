using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using System.Diagnostics;

namespace Akbura.Language;

internal readonly partial struct Blender
{
    internal readonly struct Cursor
    {
        private readonly SyntaxNodeOrToken _current;
        private readonly PathNode? _parent;
        private readonly int _indexInParent;

        public Cursor(AkburaSyntax root)
            : this(new SyntaxNodeOrToken(root), parent: null, indexInParent: 0)
        {
        }

        private Cursor(SyntaxNodeOrToken current, PathNode? parent, int indexInParent)
        {
            Debug.Assert(indexInParent >= 0);

            _current = current;
            _parent = parent;
            _indexInParent = indexInParent;
        }

        public SyntaxNodeOrToken Current
        {
            get
            {
                Debug.Assert(!IsFinished);
                return _current;
            }
        }

        public GreenNode CurrentNode => Current.RequiredUnderlyingNode;

        public int Position => Current.Position;

        public bool IsFinished => _current.UnderlyingNode == null;

        public Cursor MoveToFirstChild()
        {
            if (IsFinished || !_current.AsNode(out var node))
            {
                return MoveToNextSibling();
            }

            var children = node.ChildNodesAndTokens();
            if (TryGetFirstNonZeroWidthChild(children, out var child, out var childIndex))
            {
                return new Cursor(
                    child,
                    new PathNode(_current, _parent, _indexInParent, children),
                    childIndex);
            }

            return MoveToNextSibling();
        }

        public Cursor MoveToNextSibling()
        {
            if (IsFinished)
            {
                return default;
            }

            var parent = _parent;
            if (parent == null)
            {
                return default;
            }

            if (TryGetNextNonZeroWidthChild(
                    parent.Children,
                    _indexInParent + 1,
                    out var sibling,
                    out var siblingIndex))
            {
                return new Cursor(sibling, parent, siblingIndex);
            }

            return MoveToParent().MoveToNextSibling();
        }

        public Cursor MoveToParent()
        {
            if (IsFinished)
            {
                return default;
            }

            var parent = _parent;
            if (parent == null)
            {
                return default;
            }

            return new Cursor(parent.NodeOrToken, parent.Parent, parent.IndexInParent);
        }

        private static bool TryGetFirstNonZeroWidthChild(
            ChildSyntaxList children,
            out SyntaxNodeOrToken child,
            out int childIndex)
        {
            return TryGetNextNonZeroWidthChild(
                children,
                startIndex: 0,
                out child,
                out childIndex);
        }

        private static bool TryGetNextNonZeroWidthChild(
            ChildSyntaxList children,
            int startIndex,
            out SyntaxNodeOrToken child,
            out int childIndex)
        {
            for (var i = startIndex; i < children.Count; i++)
            {
                var current = children[i];
                if (IsNonZeroWidthOrIsEndOfFile(current))
                {
                    child = current;
                    childIndex = i;
                    return true;
                }
            }

            child = default;
            childIndex = 0;
            return false;
        }

        private static bool IsNonZeroWidthOrIsEndOfFile(SyntaxNodeOrToken nodeOrToken)
        {
            var underlying = nodeOrToken.UnderlyingNode;
            return underlying != null &&
                   (underlying.FullWidth > 0 ||
                    underlying.Kind == SyntaxKind.EndOfFileToken);
        }

        private sealed class PathNode
        {
            public readonly SyntaxNodeOrToken NodeOrToken;
            public readonly PathNode? Parent;
            public readonly int IndexInParent;
            public readonly ChildSyntaxList Children;

            public PathNode(
                SyntaxNodeOrToken nodeOrToken,
                PathNode? parent,
                int indexInParent,
                ChildSyntaxList children)
            {
                NodeOrToken = nodeOrToken;
                Parent = parent;
                IndexInParent = indexInParent;
                Children = children;
            }
        }
    }
}
