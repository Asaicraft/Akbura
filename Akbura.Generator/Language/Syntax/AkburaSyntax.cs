using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Akbura.Language.Syntax;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
internal abstract partial class AkburaSyntax
{
    private AkburaSyntax? _root;

    public AkburaSyntax(GreenNode greenNode, AkburaSyntax? parent, int position)
    {
        Green = greenNode;
        Parent = parent;
        Position = position;
    }

    public GreenNode Green
    {
        get;
    }

    public AkburaSyntax? Parent
    {
        get;
    }

    public AkburaSyntax Root
    {
        get
        {
            if (_root != null)
            {
                return _root;
            }

            if (Parent != null)
            {
                return _root = Parent.Root;
            }

            return _root = this;
        }
    }

    public int Position
    {
        get;
    }

    public int EndPosition => Position + Green.FullWidth;

    public int FullWidth => Green.FullWidth;

    public int Width => Green.Width;

    public SyntaxKind Kind => Green.Kind;

    public ushort RawKind => Green.RawKind;

    public int SpanStart => Position + Green.GetLeadingTriviaWidth();

    /// <summary>
    /// The absolute span of this node in characters, not including its leading and trailing trivia.
    /// </summary>
    public TextSpan Span
    {
        get
        {
            // Start with the full span.
            var start = Position;
            var width = Green.FullWidth;

            // adjust for preceding trivia (avoid calling this twice, do not call Green.Width)
            var precedingWidth = Green.GetLeadingTriviaWidth();
            start += precedingWidth;
            width -= precedingWidth;

            // adjust for following trivia width
            width -= this.Green.GetTrailingTriviaWidth();

            Debug.Assert(width >= 0);
            return new TextSpan(start, width);
        }
    }

    public TextSpan FullSpan => new(Position, Green.FullWidth);

    public int SlotCount => Green.SlotCount;

    public bool IsToken => Green.IsToken;

    public bool IsList => Green.IsList;
    public bool IsMissing => Green.IsMissing;


    public bool ContainsDiagnosticsDirectly => Green.ContainsDiagnosticsDirectly;
    public bool ContainsAnnotationsDirectly => Green.ContainsAnnotationsDirectly;
    public bool ContainsDiagnostics => Green.ContainsDiagnostics;
    public bool ContainsAnnotations => Green.ContainsAnnotations;
    public bool IsCSharpSyntax => Green.IsCSharpSyntax;
    public bool ContainsAkburaIdentifierInCsharpSyntax => Green.ContainsAkburaSyntaxInCSharpSyntax;

    public AkburaSyntax GetRequiredNodeSlot(int index)
    {
        var node = GetNodeSlot(index);

        return node == null ? throw new NullReferenceException() : node;
    }

    public abstract AkburaSyntax? GetNodeSlot(int index);
    public abstract AkburaSyntax? GetCachedSlot(int index);

    public AkburaSyntax? GetRed(ref AkburaSyntax? field, int slot)
    {
        var result = field;

        if (result == null)
        {
            var green = Green.GetSlot(slot);
            if (green != null)
            {
                Interlocked.CompareExchange(ref field, green.CreateRed(this, GetChildPosition(slot)), null);
                result = field;
            }
        }

        return result;
    }

    // Special case of above function where slot = 0, does not need GetChildPosition
    public AkburaSyntax? GetRedAtZero(ref AkburaSyntax? field)
    {
        var result = field;

        if (result == null)
        {
            var green = Green.GetSlot(0);
            if (green != null)
            {
                Interlocked.CompareExchange(ref field, green.CreateRed(this, Position), null);
                result = field;
            }
        }

        return result;
    }

    protected T? GetRed<T>(ref T? field, int slot)
        where T : AkburaSyntax
    {
        var result = field;

        if (result == null)
        {
            var green = Green.GetSlot(slot);
            if (green != null)
            {
                Interlocked.CompareExchange(ref field, (T)green.CreateRed(this, this.GetChildPosition(slot)), null);
                result = field;
            }
        }

        return result;
    }

    // special case of above function where slot = 0, does not need GetChildPosition
    protected T? GetRedAtZero<T>(ref T field)
        where T : AkburaSyntax
    {
        var result = field;

        if (result == null)
        {
            var green = Green.GetSlot(0);
            if (green != null)
            {
                Interlocked.CompareExchange(ref field, (T)green.CreateRed(this, Position), null);
                result = field;
            }
        }

        return result;
    }

    public AkburaSyntax? GetRedElement(ref AkburaSyntax? element, int slot)
    {
        Debug.Assert(IsList);

        var result = element;

        if (result == null)
        {
            var green = Green.GetRequiredSlot(slot);
            // passing list's parent
            Interlocked.CompareExchange(ref element, green.CreateRed(Parent, GetChildPosition(slot)), null);
            result = element;
        }

        return result;
    }

    /// <summary>
    /// special cased helper for 2 and 3 children lists where child #1 may map to a token
    /// </summary>
    public AkburaSyntax? GetRedElementIfNotToken(ref AkburaSyntax? element)
    {
        Debug.Assert(this.IsList);

        var result = element;

        if (result == null)
        {
            var green = this.Green.GetRequiredSlot(1);
            if (!green.IsToken)
            {
                // passing list's parent
                Interlocked.CompareExchange(ref element, green.CreateRed(this.Parent, this.GetChildPosition(1)), null);
                result = element;
            }
        }

        return result;
    }

    public AkburaSyntax GetWeakRedElement(ref WeakReference<AkburaSyntax>? slot, int index)
    {
        if (slot?.TryGetTarget(out var value) == true)
        {
            return value!;
        }

        return CreateWeakItem(ref slot, index);
    }

    // handle a miss
    private AkburaSyntax CreateWeakItem(ref WeakReference<AkburaSyntax>? slot, int index)
    {
        var greenChild = Green.GetRequiredSlot(index);
        var newNode = greenChild.CreateRed(Parent, GetChildPosition(index));
        var newWeakReference = new WeakReference<AkburaSyntax>(newNode);

        while (true)
        {
            var previousWeakReference = slot;
            if (previousWeakReference?.TryGetTarget(out var previousNode) == true)
            {
                return previousNode!;
            }

            if (Interlocked.CompareExchange(ref slot, newWeakReference, previousWeakReference) == previousWeakReference)
            {
                return newNode;
            }
        }
    }

    internal int GetChildIndex(int slot)
    {
        var index = 0;

        for (var i = 0; i < slot; i++)
        {
            var item = Green.GetSlot(i);
            if (item != null)
            {
                if (item.IsList)
                {
                    index += item.SlotCount;
                }
                else
                {
                    index++;
                }
            }
        }

        return index;
    }

    public virtual int GetChildPosition(int index)
    {
        var offset = 0;
        var green = Green;

        while (index > 0)
        {
            index--;
            var prevSibling = GetCachedSlot(index);
            if (prevSibling != null)
            {
                return prevSibling.EndPosition + offset;
            }

            var greenChild = green.GetSlot(index);
            if (greenChild != null)
            {
                offset += greenChild.FullWidth;
            }
        }

        return Position + offset;
    }

    // Similar to GetChildPosition() but calculating based on the positions of
    // following siblings rather than previous siblings.
    public int GetChildPositionFromEnd(int index)
    {
        if (GetCachedSlot(index) is { } node)
        {
            return node.Position;
        }

        var green = Green;
        var offset = green.GetSlot(index)?.FullWidth ?? 0;
        var slotCount = green.SlotCount;

        while (index < slotCount - 1)
        {
            index++;
            var nextSibling = GetCachedSlot(index);
            if (nextSibling != null)
            {
                return nextSibling.Position - offset;
            }
            var greenChild = green.GetSlot(index);
            if (greenChild != null)
            {
                offset += greenChild.FullWidth;
            }
        }

        return EndPosition - offset;
    }

    public ImmutableArray<AkburaDiagnostic> GetDiagnostics() => Green.GetDiagnostics();

    #region Annotations

    public ImmutableArray<AkburaSyntaxAnnotation> GetAnnotations() => Green.GetAnnotations();

    public AkburaSyntax WithoutAnnotations(IEnumerable<AkburaSyntaxAnnotation> annotations)
    {
        var green = Green.WithoutAnnotations(annotations)!;

        Debug.Assert(green is not null);

        return green!.CreateRed();
    }

    public AkburaSyntax WithAdditionalAnnotations(IEnumerable<AkburaSyntaxAnnotation> additionalAnnotations)
    {
        var green = Green.AddAnnotations(additionalAnnotations)!;

        Debug.Assert(green is not null);

        return green!.CreateRed();
    }

    public IEnumerable<AkburaSyntaxAnnotation> GetAnnotations(IEnumerable<string> annotationKinds)
    {
        return Green.GetAnnotations(annotationKinds);
    }

    public IEnumerable<AkburaSyntaxAnnotation> GetAnnotations(string annotationKind)
    {
        return Green.GetAnnotations(annotationKind);
    }

    public bool HasAnnotations(string annotationKind)
    {
        return Green.HasAnnotations(annotationKind);
    }

    public bool HasAnnotations(IEnumerable<string> annotationKinds)
    {
        return Green.HasAnnotations(annotationKinds);
    }

    public bool HasAnnotation([NotNullWhen(true)] AkburaSyntaxAnnotation? annotation)
    {
        return Green.HasAnnotation(annotation);
    }

    #endregion

    public bool IsEquivalentTo(AkburaSyntax? other)
    {
        if (this == other)
        {
            return true;
        }

        if (other == null)
        {
            return false;
        }

        return Green.IsEquivalentTo(other.Green);
    }

    public override string ToString()
    {
        return Green.ToString();
    }

    public string ToFullString()
    {
        return Green.ToFullString();
    }

    protected virtual string GetDebuggerDisplay()
    {
        if (IsToken)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0};[{1}]", Kind, ToString());
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} [{1}..{2})", Kind, Position, EndPosition);
    }

    public void WriteTo(TextWriter writer)
    {
        Green.WriteTo(writer);
    }

    #region Token Lookup
    /// <summary>
    /// Finds a descendant token of this node whose span includes the supplied position. 
    /// </summary>
    /// <param name="position">The character position of the token relative to the beginning of the file.</param>
    /// <param name="findInsideTrivia">
    /// True to return tokens that are part of trivia. If false finds the token whose full span (including trivia)
    /// includes the position.
    /// </param>
    public SyntaxToken FindToken(int position)
    {
        return FindTokenCore(position);
    }

    /// <summary>
    /// Gets the first token of the tree rooted by this node. Skips zero-width tokens.
    /// </summary>
    /// <returns>The first token or <c>default(SyntaxToken)</c> if it doesn't exist.</returns>
    public SyntaxToken GetFirstToken(bool includeZeroWidth = false)
    {
        return SyntaxNavigator.GetFirstToken(this, includeZeroWidth);
    }

    /// <summary>
    /// Gets the last token of the tree rooted by this node. Skips zero-width tokens.
    /// </summary>
    /// <returns>The last token or <c>default(SyntaxToken)</c> if it doesn't exist.</returns>
    public SyntaxToken GetLastToken(bool includeZeroWidth = false)
    {
        return SyntaxNavigator.GetLastToken(this, includeZeroWidth);
    }

    /// <summary>
    /// The list of child nodes and tokens of this node, where each element is a SyntaxNodeOrToken instance.
    /// </summary>
    public ChildSyntaxList ChildNodesAndTokens()
    {
        return new ChildSyntaxList(this);
    }

    public virtual SyntaxNodeOrToken ChildThatContainsPosition(int position)
    {
        //PERF: it is very important to keep this method fast.

        if (!FullSpan.Contains(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var childNodeOrToken = ChildSyntaxList.ChildThatContainsPosition(this, position);
        Debug.Assert(childNodeOrToken.FullSpan.Contains(position), "ChildThatContainsPosition's return value does not contain the requested position.");
        return childNodeOrToken;
    }

    /// <summary>
    /// Gets a list of the direct child tokens of this node.
    /// </summary>
    public IEnumerable<SyntaxToken> ChildTokens()
    {
        foreach (var nodeOrToken in this.ChildNodesAndTokens())
        {
            if (nodeOrToken.IsToken)
            {
                yield return nodeOrToken.AsToken();
            }
        }
    }

    /// <summary>
    /// Gets a list of all the tokens in the span of this node.
    /// </summary>
    public IEnumerable<SyntaxToken> DescendantTokens(Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesAndTokens(descendIntoChildren, descendIntoTrivia).Where(sn => sn.IsToken).Select(sn => sn.AsToken());
    }

    /// <summary>
    /// Gets a list of all the tokens in the full span of this node.
    /// </summary>
    public IEnumerable<SyntaxToken> DescendantTokens(TextSpan span, Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesAndTokens(span, descendIntoChildren, descendIntoTrivia).Where(sn => sn.IsToken).Select(sn => sn.AsToken());
    }

    #endregion

    #region Trivia Lookup
    /// <summary>
    /// The list of trivia that appears before this node in the source code and are attached to a token that is a
    /// descendant of this node.
    /// </summary>
    public SyntaxTriviaList GetLeadingTrivia()
    {
        return GetFirstToken(includeZeroWidth: true).LeadingTrivia;
    }

    /// <summary>
    /// The list of trivia that appears after this node in the source code and are attached to a token that is a
    /// descendant of this node.
    /// </summary>
    public SyntaxTriviaList GetTrailingTrivia()
    {
        return GetLastToken(includeZeroWidth: true).TrailingTrivia;
    }

    /// <summary>
    /// Finds a descendant trivia of this node at the specified position, where the position is
    /// within the span of the node.
    /// </summary>
    /// <param name="position">The character position of the trivia relative to the beginning of
    /// the file.</param>
    /// <returns></returns>
    public SyntaxTrivia FindTrivia(int position)
    {
        if (FullSpan.Contains(position))
        {
            return FindTriviaByOffset(this, position - Position);
        }

        return default;
    }

    public static SyntaxTrivia FindTriviaByOffset(AkburaSyntax node, int textOffset)
    {
    recurse:
        if (textOffset >= 0)
        {
            foreach (var element in node.ChildNodesAndTokens())
            {
                var fullWidth = element.FullWidth;
                if (textOffset < fullWidth)
                {
                    if (element.AsNode(out var elementNode))
                    {
                        node = elementNode;
                        goto recurse;
                    }
                    else if (element.IsToken)
                    {
                        var token = element.AsToken();
                        var leading = token.LeadingWidth;
                        if (textOffset < token.LeadingWidth)
                        {
                            foreach (var trivia in token.LeadingTrivia)
                            {
                                if (textOffset < trivia.FullWidth)
                                {
                                    return trivia;
                                }

                                textOffset -= trivia.FullWidth;
                            }
                        }
                        else if (textOffset >= leading + token.Width)
                        {
                            textOffset -= leading + token.Width;
                            foreach (var trivia in token.TrailingTrivia)
                            {
                                if (textOffset < trivia.FullWidth)
                                {

                                    return trivia;
                                }

                                textOffset -= trivia.FullWidth;
                            }
                        }

                        return default;
                    }
                }

                textOffset -= fullWidth;
            }
        }

        return default;
    }

    /// <summary>
    /// Get a list of all the trivia associated with the descendant nodes and tokens.
    /// </summary>
    public IEnumerable<SyntaxTrivia> DescendantTrivia(Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantTriviaImpl(this.FullSpan, descendIntoChildren, descendIntoTrivia);
    }

    /// <summary>
    /// Get a list of all the trivia associated with the descendant nodes and tokens.
    /// </summary>
    public IEnumerable<SyntaxTrivia> DescendantTrivia(TextSpan span, Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantTriviaImpl(span, descendIntoChildren, descendIntoTrivia);
    }

    #endregion

    #region Core Methods

    /// <summary>
    /// Finds a descendant token of this node whose span includes the supplied position. 
    /// </summary>
    /// <param name="position">The character position of the token relative to the beginning of the file.</param>
    protected virtual SyntaxToken FindTokenCore(int position)
    {
        if (TryGetEofAt(position, out var EoF))
        {
            return EoF;
        }

        if (!FullSpan.Contains(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return FindTokenInternal(position);
    }

    private bool TryGetEofAt(int position, out SyntaxToken Eof)
    {
        if (position == EndPosition)
        {
            if (this is ICompilationUnitSyntax compilationUnit)
            {
                Eof = compilationUnit.EndOfFileToken;
                Debug.Assert(Eof.EndPosition == position);
                return true;
            }
        }

        Eof = default;
        return false;
    }

    public SyntaxToken FindTokenInternal(int position)
    {
        // While maintaining invariant curNode.Position <= position < curNode.FullSpan.End
        // go down the tree until a token is found
        SyntaxNodeOrToken curNode = this;

        while (true)
        {
            Debug.Assert(curNode.RawKind != 0);
            Debug.Assert(curNode.FullSpan.Contains(position));

            var node = curNode.AsNode();

            if (node != null)
            {
                //find a child that includes the position
                curNode = node.ChildThatContainsPosition(position);
            }
            else
            {
                return curNode.AsToken();
            }
        }
    }

    /// <summary>
    /// Gets a list of ancestor nodes
    /// </summary>
    public IEnumerable<AkburaSyntax> Ancestors()
    {
        return this.Parent?
            .AncestorsAndSelf() ??
            [];
    }

    /// <summary>
    /// Gets a list of ancestor nodes (including this node) 
    /// </summary>
    public IEnumerable<AkburaSyntax> AncestorsAndSelf()
    {
        for (var node = this; node != null; node = GetParent(node))
        {
            yield return node;
        }
    }

    private static AkburaSyntax? GetParent(AkburaSyntax node)
    {
        var parent = node.Parent;
        //if (parent == null && ascendOutOfTrivia)
        //{
        //    if (node is IStructuredTriviaSyntax structuredTrivia)
        //    {
        //        parent = structuredTrivia.ParentTrivia.Token.Parent;
        //    }
        //}

        return parent;
    }

    /// <summary>
    /// Gets the first node of type TNode that matches the predicate.
    /// </summary>
    public TNode? FirstAncestorOrSelf<TNode>(Func<TNode, bool>? predicate = null)
        where TNode : AkburaSyntax
    {
        for (var node = this; node != null; node = GetParent(node))
        {
            if (node is TNode tnode && (predicate == null || predicate(tnode)))
            {
                return tnode;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the first node of type TNode that matches the predicate.
    /// </summary>
    public TNode? FirstAncestorOrSelf<TNode, TArg>(Func<TNode, TArg, bool> predicate, TArg argument)
        where TNode : AkburaSyntax
    {
        for (var node = this; node != null; node = GetParent(node))
        {
            if (node is TNode tnode && predicate(tnode, argument))
            {
                return tnode;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a list of descendant nodes in prefix document order.
    /// </summary>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<AkburaSyntax> DescendantNodes(Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesImpl(FullSpan, descendIntoChildren, descendIntoTrivia, includeSelf: false);
    }

    /// <summary>
    /// Gets a list of descendant nodes in prefix document order.
    /// </summary>
    /// <param name="span">The span the node's full span must intersect.</param>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<AkburaSyntax> DescendantNodes(TextSpan span, Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesImpl(span, descendIntoChildren, descendIntoTrivia, includeSelf: false);
    }

    /// <summary>
    /// Gets a list of descendant nodes (including this node) in prefix document order.
    /// </summary>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<AkburaSyntax> DescendantNodesAndSelf(Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesImpl(FullSpan, descendIntoChildren, descendIntoTrivia, includeSelf: true);
    }

    /// <summary>
    /// Gets a list of descendant nodes (including this node) in prefix document order.
    /// </summary>
    /// <param name="span">The span the node's full span must intersect.</param>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<AkburaSyntax> DescendantNodesAndSelf(TextSpan span, Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesImpl(span, descendIntoChildren, descendIntoTrivia, includeSelf: true);
    }

    /// <summary>
    /// Gets a list of descendant nodes and tokens in prefix document order.
    /// </summary>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokens(Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesAndTokensImpl(FullSpan, descendIntoChildren, descendIntoTrivia, includeSelf: false);
    }

    /// <summary>
    /// Gets a list of the descendant nodes and tokens in prefix document order.
    /// </summary>
    /// <param name="span">The span the node's full span must intersect.</param>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokens(TextSpan span, Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesAndTokensImpl(span, descendIntoChildren, descendIntoTrivia, includeSelf: false);
    }

    /// <summary>
    /// Gets a list of descendant nodes and tokens (including this node) in prefix document order.
    /// </summary>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokensAndSelf(Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesAndTokensImpl(FullSpan, descendIntoChildren, descendIntoTrivia, includeSelf: true);
    }

    /// <summary>
    /// Gets a list of the descendant nodes and tokens (including this node) in prefix document order.
    /// </summary>
    /// <param name="span">The span the node's full span must intersect.</param>
    /// <param name="descendIntoChildren">An optional function that determines if the search descends into the argument node's children.</param>
    /// <param name="descendIntoTrivia">Determines if nodes that are part of structured trivia are included in the list.</param>
    public IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokensAndSelf(TextSpan span, Func<AkburaSyntax, bool>? descendIntoChildren = null, bool descendIntoTrivia = false)
    {
        return DescendantNodesAndTokensImpl(span, descendIntoChildren, descendIntoTrivia, includeSelf: true);
    }

    /// <summary>
    /// Finds the node with the smallest <see cref="FullSpan"/> that contains <paramref name="span"/>.
    /// <paramref name="getInnermostNodeForTie"/> is used to determine the behavior in case of a tie (i.e. a node having the same span as its parent).
    /// If <paramref name="getInnermostNodeForTie"/> is true, then it returns lowest descending node encompassing the given <paramref name="span"/>.
    /// Otherwise, it returns the outermost node encompassing the given <paramref name="span"/>.
    /// </summary>
    /// <devdoc>
    /// TODO: This should probably be reimplemented with <see cref="ChildThatContainsPosition"/>
    /// </devdoc>
    /// <exception cref="ArgumentOutOfRangeException">This exception is thrown if <see cref="FullSpan"/> doesn't contain the given span.</exception>
    public AkburaSyntax FindNode(TextSpan span, bool getInnermostNodeForTie = false)
    {
        if (!FullSpan.Contains(span))
        {
            throw new ArgumentOutOfRangeException(nameof(span));
        }

        var node = FindToken(span.Start)
            .Parent
            !.FirstAncestorOrSelf<AkburaSyntax, TextSpan>((a, span) => a.FullSpan.Contains(span), span);

        Debug.Assert(node is not null);
        var cuRoot = Root;

        // Tie-breaking.
        if (!getInnermostNodeForTie)
        {
            while (true)
            {
                var parent = node!.Parent;
                // NOTE: We care about FullSpan equality, but FullWidth is cheaper and equivalent.
                if (parent == null || parent.FullWidth != node.FullWidth)
                {
                    break;
                }
                // prefer child over compilation unit
                if (parent == cuRoot)
                {
                    break;
                }

                node = parent;
            }
        }

        return node!;
    }

    public static SyntaxTrivia GetTriviaFromSyntaxToken(int position, in SyntaxToken token)
    {
        var span = token.Span;
        var trivia = new SyntaxTrivia();
        if (position < span.Start && token.HasLeadingTrivia)
        {
            trivia = GetTriviaThatContainsPosition(token.LeadingTrivia, position);
        }
        else if (position >= span.End && token.HasTrailingTrivia)
        {
            trivia = GetTriviaThatContainsPosition(token.TrailingTrivia, position);
        }

        return trivia;
    }

    public static SyntaxTrivia GetTriviaThatContainsPosition(in SyntaxTriviaList list, int position)
    {
        foreach (var trivia in list)
        {
            if (trivia.FullSpan.Contains(position))
            {
                return trivia;
            }

            if (trivia.Position > position)
            {
                break;
            }
        }

        return default;
    }

    #endregion

    #region Syntax Visitor
    public virtual void Accept(SyntaxVisitor visitor)
    {
        visitor.DefaultVisit(this);
    }

    public virtual TResult? Accept<TResult>(SyntaxVisitor<TResult> syntaxVisitor)
    {
        return syntaxVisitor.DefaultVisit(this);
    }

    public virtual TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> syntaxVisitor, TParameter parameter)
    {
        return syntaxVisitor.DefaultVisit(this, parameter);
    }
    #endregion

    #region Replace, Insert, and Remove

    public AkburaSyntax Replace<TNode>(
        IEnumerable<TNode>? nodes = null,
        Func<TNode, TNode, AkburaSyntax>? computeReplacementNode = null,
        IEnumerable<SyntaxToken>? tokens = null,
        Func<SyntaxToken, SyntaxToken, SyntaxToken>? computeReplacementToken = null,
        IEnumerable<SyntaxTrivia>? trivia = null,
        Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia>? computeReplacementTrivia = null)
    where TNode : AkburaSyntax
    {
        return SyntaxReplacer.Replace(
            this,
            nodes,
            computeReplacementNode,
            tokens,
            computeReplacementToken,
            trivia,
            computeReplacementTrivia);
    }

    public AkburaSyntax ReplaceNodeInList(AkburaSyntax originalNode, IEnumerable<AkburaSyntax> replacementNodes)
    {
        return SyntaxReplacer.ReplaceNodeInList(this, originalNode, replacementNodes);
    }

    public AkburaSyntax InsertNodesInList(AkburaSyntax nodeInList, IEnumerable<AkburaSyntax> nodesToInsert, bool insertBefore)
    {
        return SyntaxReplacer.InsertNodeInList(this, nodeInList, nodesToInsert, insertBefore);
    }

    public AkburaSyntax ReplaceTokenInList(SyntaxToken originalToken, IEnumerable<SyntaxToken> newTokens)
    {
        return SyntaxReplacer.ReplaceTokenInList(this, originalToken, newTokens);
    }

    public AkburaSyntax InsertTokensInList(SyntaxToken originalToken, IEnumerable<SyntaxToken> newTokens, bool insertBefore)
    {
        return SyntaxReplacer.InsertTokenInList(this, originalToken, newTokens, insertBefore);
    }

    public AkburaSyntax ReplaceTriviaInList(SyntaxTrivia originalTrivia, IEnumerable<SyntaxTrivia> newTrivia)
    {
        return SyntaxReplacer.ReplaceTriviaInList(this, originalTrivia, newTrivia);
    }

    public AkburaSyntax InsertTriviaInList(SyntaxTrivia originalTrivia, IEnumerable<SyntaxTrivia> newTrivia, bool insertBefore)
    {
        return SyntaxReplacer.InsertTriviaInList(this, originalTrivia, newTrivia, insertBefore);
    }

    /// <summary>
    /// Creates a new tree of nodes with the specified old token replaced with a new token.
    /// </summary>
    /// <typeparam name="TRoot">The type of the root node.</typeparam>
    /// <param name="root">The root node of the tree of nodes.</param>
    /// <param name="oldToken">The token to be replaced.</param>
    /// <param name="newToken">The new token to use in the new tree in place of the old
    /// token.</param>
    public AkburaSyntax? ReplaceToken(SyntaxToken oldToken, SyntaxToken newToken)
    {
        return Replace<AkburaSyntax>(tokens: [oldToken], computeReplacementToken: (o, r) => newToken);
    }

    /// <summary>
    /// Creates a new tree of nodes with the specified trivia replaced with new trivia.
    /// </summary>
    /// <typeparam name="TRoot">The type of the root node.</typeparam>
    /// <param name="root">The root node of the tree of nodes.</param>
    /// <param name="trivia">The trivia to be replaced; descendants of the root node.</param>
    /// <param name="computeReplacementTrivia">A function that computes replacement trivia for
    /// the specified arguments. The first argument is the original trivia. The second argument is
    /// the same trivia with potentially rewritten sub structure.</param>
    public AkburaSyntax ReplaceTrivia(IEnumerable<SyntaxTrivia> trivia, Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia> computeReplacementTrivia)
    {
        return Replace<AkburaSyntax>(trivia: trivia, computeReplacementTrivia: computeReplacementTrivia);
    }

    /// <summary>
    /// Creates a new tree of nodes with the specified trivia replaced with new trivia.
    /// </summary>
    /// <typeparam name="TRoot">The type of the root node.</typeparam>
    /// <param name="root">The root node of the tree of nodes.</param>
    /// <param name="trivia">The trivia to be replaced.</param>
    /// <param name="newTrivia">The new trivia to use in the new tree in place of the old trivia.</param>
    public AkburaSyntax ReplaceTrivia(SyntaxTrivia trivia, SyntaxTrivia newTrivia)
    {
        return Replace<AkburaSyntax>(trivia: [trivia], computeReplacementTrivia: (o, r) => newTrivia);
    }

    public AkburaSyntax? RemoveNodes(IEnumerable<AkburaSyntax> nodes, SyntaxRemoveOptions options)
    {
        return SyntaxNodeRemover.RemoveNodes(this, nodes, options);
    }

    #endregion

    public AkburaSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
    {
        var last = GetLastToken(includeZeroWidth: true);
        var newLast = last.WithTrailingTrivia(trivia);
        return ReplaceToken(last, newLast)!;
    }

    public AkburaSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
    {
        var first = GetFirstToken(includeZeroWidth: true);
        var newFirst = first.WithLeadingTrivia(trivia);
        return ReplaceToken(first, newFirst)!;
    }
}
