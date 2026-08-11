// Design In-Memory File System  (LeetCode 588, "Design File System" family)
// Difficulty: Hard (the base four methods are Medium; the follow-ups are where it goes)
// Pattern: n-ary tree keyed by path component; one walk primitive, four thin wrappers
//
// This is a DESIGN question, so the code is the easy half. The interesting half is
// the contract, and every one of these is a real fork in the implementation:
//
//   1. What does ls on a FILE return?         -> ["file.txt"], not its content and
//                                                not an error. Stated in the prompt,
//                                                and it is the one rule people skip.
//   2. Does addContentToFile overwrite?       -> APPENDS. "add", not "set".
//   3. Do mkdir / addContentToFile create      -> Yes, both. mkdir -p semantics, so
//      missing intermediate directories?         neither ever fails on a missing parent.
//   4. What is "lexicographical order"?        -> ORDINAL. See the note below; this is
//                                                the one that silently fails in C#.
//   5. What happens on a bad path -- ls of a   -> The prompt is silent, so the choice is
//      directory that does not exist, reading     yours: throw or return empty. Throwing
//      a directory as a file, mkdir over a        is right (a silent empty list hides the
//      file?                                      caller's bug) but SAY that you chose it.
//   6. Are paths absolute? Are . and .. real?  -> Absolute per the constraints. Dots are
//                                                legal NAME characters here ("file.txt"),
//                                                so "." is a name, not "current dir".
//                                                Normalize() below implements the other
//                                                reading, kept deliberately separate.
//
// THE ORDERING TRAP. "Lexicographical" means compare by character code: uppercase
// letters sort before lowercase ones, so ["b", "A"] -> ["A", "b"]. C#'s default
// string comparison is CULTURE-AWARE and gives ["A", "b"] too -- but for the wrong
// reason, and on other inputs it disagrees outright: culture ordering puts "a"
// before "B", ordinal puts "B" before "a". So `names.Sort()` and
// `new SortedDictionary<string, Node>()` are both BUGS here, and they are bugs that
// pass every test whose names are all lowercase. StringComparer.Ordinal is the fix,
// and naming it unprompted is worth more in an interview than the rest of the file.
//
// THE SHAPE. A directory is a node with children; a file is a node with content.
// Children live in a SortedDictionary keyed ordinally, which is the whole design
// decision in one line:
//
//                            lookup     insert     ls (k children)
//   SortedDictionary         O(log k)   O(log k)   O(k)         <- chosen
//   Dictionary + sort at ls  O(1)       O(1)       O(k log k)
//
// ls is the rare operation and directories are small, so either is defensible --
// but the sorted map keeps ls free of allocation-heavy sorting and makes "the
// listing is always ordered" an invariant of the structure instead of a promise
// the ls method has to keep. If writes dominated (a build-tool cache, say), the
// hash map plus a lazy sort would be the better trade.
//
// With that, every public method is the same two steps -- split the path, walk the
// components -- so the whole API is:
//
//   Mkdir                  walk with create=true                 O(p log k)
//   AddContentToFile       walk to the parent, append            O(p log k + |content|)
//   ReadContentFromFile    walk, must land on a file             O(p log k + |content|)
//   Ls                     walk, list children in order          O(p log k + c)
//   Delete / Move / Copy   walk + detach / attach                O(p log k) (+ subtree for copy)
//   Find                   glob match, prunes on literal parts   O(matched subtree)
//
// where p = number of path components and k = children per directory. There is no
// clever algorithm anywhere in this problem; the grade comes from the contract, the
// ordering, and the error cases.
//
// APPENDING. Content is a StringBuilder, not a string. n appends of k characters
// each is O(nk) amortized into the builder versus O(n^2 k) for `content += chunk`,
// which copies the whole file every time. Files are exactly the thing that gets
// appended to in a loop, so this is not premature.
//
// THREAD SAFETY. Included rather than hand-waved: a single ReaderWriterLockSlim
// around the tree. Concurrent ls/read run in parallel; any mutation is exclusive.
// That is the correct FIRST answer -- see the notes at the bottom for why a real
// file system does not stop there.

using System.Text;

namespace CodingPatterns.Trees;

/// <summary>
/// Hierarchical in-memory file system over absolute paths. Directory listings are
/// always in ordinal (character-code) lexicographical order.
/// </summary>
public sealed class InMemoryFileSystem
{
    // ------------------------------------------------------------------- node

    /// <summary>
    /// One entry in the tree. A node is EITHER a file (has content) or a
    /// directory (has children) -- never both, never neither, and the two
    /// backing fields are what enforce that rather than a bool anyone can set.
    /// </summary>
    private sealed class Node
    {
        private readonly StringBuilder _content;
        private readonly SortedDictionary<string, Node> _children;

        private Node(string name, bool isFile)
        {
            Name = name;
            if (isFile)
                _content = new StringBuilder();
            else
                // Ordinal, NOT the default comparer: see the header. This single
                // argument is the difference between correct and plausible.
                _children = new SortedDictionary<string, Node>(StringComparer.Ordinal);
        }

        public static Node NewFile(string name) => new(name, isFile: true);

        public static Node NewDirectory(string name) => new(name, isFile: false);

        public string Name { get; private set; }

        public bool IsFile => _content is not null;

        public StringBuilder Content =>
            _content ?? throw new InvalidOperationException($"'{Name}' is a directory, not a file");

        public SortedDictionary<string, Node> Children =>
            _children ?? throw new InvalidOperationException($"'{Name}' is a file, not a directory");

        public void Rename(string name) => Name = name;

        /// <summary>Deep copy -- used by <see cref="Copy"/>, so the two trees never alias.</summary>
        public Node Clone(string name)
        {
            if (IsFile)
            {
                var file = NewFile(name);
                file.Content.Append(_content);
                return file;
            }

            var directory = NewDirectory(name);
            foreach (var (childName, child) in _children)
                directory.Children[childName] = child.Clone(childName);
            return directory;
        }
    }

    private static readonly char[] Wildcards = { '*', '?' };

    private readonly Node _root = Node.NewDirectory("/");
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    // ------------------------------------------------------------- the API

    /// <summary>
    /// Creates the directory at <paramref name="path"/> and every missing
    /// directory above it (mkdir -p). Creating a directory that already exists is
    /// a no-op, not an error -- but a component that already exists as a FILE is,
    /// because there is no sane way to continue.
    /// </summary>
    public void Mkdir(string path)
    {
        var parts = Split(path);
        Write(() => { Descend(parts, parts.Length, create: true, path); });
    }

    /// <summary>
    /// Appends <paramref name="content"/> to the file at <paramref name="path"/>,
    /// creating the file -- and any missing parent directories -- if needed.
    /// Appends, never overwrites; see <see cref="WriteContentToFile"/> for that.
    /// </summary>
    public void AddContentToFile(string path, string content)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        Write(() => FileFor(path, create: true).Content.Append(content));
    }

    /// <summary>Returns the full content of the file at <paramref name="path"/>.</summary>
    public string ReadContentFromFile(string path)
    {
        return Read(() =>
        {
            var node = Resolve(path);
            if (!node.IsFile)
                throw new InvalidOperationException($"'{path}' is a directory, not a file");
            return node.Content.ToString();
        });
    }

    /// <summary>
    /// Lists <paramref name="path"/>: the direct children in ordinal
    /// lexicographical order for a directory, or the single file name for a file.
    /// The file case is not a curiosity -- it mirrors `ls foo.txt` in a shell, and
    /// the prompt calls for it explicitly.
    /// </summary>
    public IReadOnlyList<string> Ls(string path)
    {
        return Read(() =>
        {
            var node = Resolve(path);
            if (node.IsFile)
                return (IReadOnlyList<string>)new[] { node.Name };

            // Already ordered: the sorted map holds the invariant, not this method.
            return node.Children.Keys.ToArray();
        });
    }

    // --------------------------------------------------------- the follow-ups

    /// <summary>Does anything -- file or directory -- exist at <paramref name="path"/>?</summary>
    public bool Exists(string path) => Read(() => TryResolve(path) is not null);

    /// <summary>True when <paramref name="path"/> exists AND is a file.</summary>
    public bool IsFile(string path) => Read(() => TryResolve(path) is { IsFile: true });

    /// <summary>True when <paramref name="path"/> exists AND is a directory.</summary>
    public bool IsDirectory(string path) => Read(() => TryResolve(path) is { IsFile: false });

    /// <summary>Replaces a file's content outright (the `&gt;` to AddContentToFile's `&gt;&gt;`).</summary>
    public void WriteContentToFile(string path, string content)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        Write(() =>
        {
            var file = FileFor(path, create: true);
            file.Content.Clear();
            file.Content.Append(content);
        });
    }

    /// <summary>
    /// Removes a file, or a directory and everything under it (rm -r). Returns
    /// false when there was nothing there. Unlinking the subtree is O(1): dropping
    /// the parent's reference makes the whole thing unreachable, and the GC does
    /// the rest -- no recursive walk needed.
    /// </summary>
    public bool Delete(string path)
    {
        var parts = Split(path);
        if (parts.Length == 0)
            throw new InvalidOperationException("cannot delete the root directory");

        return Write(() =>
        {
            var parent = TryDescend(parts, parts.Length - 1);
            if (parent is null || parent.IsFile)
                return false;
            return parent.Children.Remove(parts[^1]);
        });
    }

    /// <summary>
    /// Moves (renames) <paramref name="source"/> to <paramref name="destination"/>.
    /// Missing parent directories of the destination are created.
    ///
    /// The bug to avoid: moving a directory INTO ITS OWN SUBTREE
    /// (/a -> /a/b/a) detaches the subtree and reattaches it to a node that is now
    /// only reachable through itself -- a cycle, and the tree is silently
    /// corrupted. Real file systems reject this (EINVAL); so does this one.
    /// </summary>
    public void Move(string source, string destination)
    {
        var from = Split(source);
        var to = Split(destination);

        if (from.Length == 0 || to.Length == 0)
            throw new InvalidOperationException("cannot move the root directory");

        Write(() =>
        {
            var node = Descend(from, from.Length, create: false, source);
            if (IsPrefix(from, to))
                throw new InvalidOperationException($"cannot move '{source}' into its own subtree '{destination}'");

            var sourceParent = Descend(from, from.Length - 1, create: false, source);
            var targetParent = Descend(to, to.Length - 1, create: true, destination);

            sourceParent.Children.Remove(from[^1]);
            node.Rename(to[^1]);
            targetParent.Children[to[^1]] = node;   // overwrites the destination, like mv
        });
    }

    /// <summary>Deep-copies <paramref name="source"/> to <paramref name="destination"/> (cp -r).</summary>
    public void Copy(string source, string destination)
    {
        var from = Split(source);
        var to = Split(destination);

        if (from.Length == 0 || to.Length == 0)
            throw new InvalidOperationException("cannot copy over the root directory");

        Write(() =>
        {
            var node = Descend(from, from.Length, create: false, source);
            if (IsPrefix(from, to))
                throw new InvalidOperationException($"cannot copy '{source}' into its own subtree '{destination}'");

            var targetParent = Descend(to, to.Length - 1, create: true, destination);
            targetParent.Children[to[^1]] = node.Clone(to[^1]);
        });
    }

    /// <summary>Total bytes (characters) of every file at or under <paramref name="path"/> -- du.</summary>
    public int TotalSize(string path = "/")
    {
        return Read(() =>
        {
            int Sum(Node node) => node.IsFile
                ? node.Content.Length
                : node.Children.Values.Sum(Sum);

            return Sum(Resolve(path));
        });
    }

    /// <summary>
    /// Every path matching an absolute glob, e.g. <c>/a/*/*.txt</c>. <c>*</c>
    /// matches any run of characters WITHIN one component and <c>?</c> exactly one;
    /// neither crosses a <c>/</c>. Literal components are followed by direct lookup,
    /// so only the wildcard levels fan out -- that pruning is the entire point, and
    /// it is why this is not "list everything, then filter".
    /// </summary>
    public IReadOnlyList<string> Find(string pattern)
    {
        var parts = Split(pattern);

        return Read(() =>
        {
            var matches = new List<string>();
            var trail = new string[parts.Length];

            void Walk(Node node, int depth)
            {
                if (depth == parts.Length)
                {
                    matches.Add("/" + string.Join('/', trail));
                    return;
                }

                if (node.IsFile)
                    return;                       // pattern goes deeper than the tree

                var part = parts[depth];
                if (part.IndexOfAny(Wildcards) < 0)
                {
                    // Literal: one O(log k) lookup instead of scanning the directory.
                    if (node.Children.TryGetValue(part, out var child))
                    {
                        trail[depth] = part;
                        Walk(child, depth + 1);
                    }
                    return;
                }

                foreach (var (name, child) in node.Children)
                {
                    if (!GlobMatches(part, name))
                        continue;
                    trail[depth] = name;
                    Walk(child, depth + 1);
                }
            }

            Walk(_root, 0);
            return (IReadOnlyList<string>)matches;
        });
    }

    /// <summary>Renders the tree, directories with a trailing slash and files with their size.</summary>
    public string Tree(string path = "/")
    {
        return Read(() =>
        {
            var sb = new StringBuilder();

            void Print(Node node, string indent)
            {
                sb.AppendLine(node.IsFile
                    ? $"{indent}{node.Name}  ({node.Content.Length}B)"
                    : $"{indent}{node.Name.TrimEnd('/')}/");

                if (node.IsFile)
                    return;
                foreach (var child in node.Children.Values)
                    Print(child, indent + "  ");
            }

            Print(Resolve(path), string.Empty);
            return sb.ToString().TrimEnd();
        });
    }

    // ------------------------------------------------------- path plumbing

    /// <summary>
    /// Splits an absolute path into components. Empty segments are dropped, so
    /// "/a//b/" and "/a/b" are the same path and "/" is zero components -- the
    /// root. Anything not starting with '/' is rejected rather than guessed at.
    /// </summary>
    private static string[] Split(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("path is empty; expected an absolute path like '/a/b'", nameof(path));
        if (path[0] != '/')
            throw new ArgumentException($"path must be absolute (start with '/'): '{path}'", nameof(path));

        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Walks the first <paramref name="count"/> components. With
    /// <paramref name="create"/> it creates missing directories along the way; without,
    /// a missing component throws. Either way, hitting a FILE mid-path throws --
    /// "/a/file.txt/b" cannot be made to mean anything.
    /// </summary>
    private Node Descend(string[] parts, int count, bool create, string path)
    {
        var node = _root;

        for (int i = 0; i < count; i++)
        {
            if (node.IsFile)
                throw new InvalidOperationException(
                    $"'{Join(parts, i)}' is a file, so '{path}' cannot be resolved");

            if (!node.Children.TryGetValue(parts[i], out var child))
            {
                if (!create)
                    throw new DirectoryNotFoundException($"no such file or directory: '{path}'");
                child = Node.NewDirectory(parts[i]);
                node.Children[parts[i]] = child;
            }

            node = child;
        }

        return node;
    }

    /// <summary>Non-throwing walk: null when any component is missing or a file blocks the way.</summary>
    private Node TryDescend(string[] parts, int count)
    {
        var node = _root;

        for (int i = 0; i < count; i++)
        {
            if (node.IsFile || !node.Children.TryGetValue(parts[i], out node))
                return null;
        }

        return node;
    }

    private Node Resolve(string path)
    {
        var parts = Split(path);
        return Descend(parts, parts.Length, create: false, path);
    }

    private Node TryResolve(string path)
    {
        var parts = Split(path);
        return TryDescend(parts, parts.Length);
    }

    /// <summary>Walks to the parent and returns the file entry, optionally creating it.</summary>
    private Node FileFor(string path, bool create)
    {
        var parts = Split(path);
        if (parts.Length == 0)
            throw new InvalidOperationException("'/' is the root directory, not a file");

        var parent = Descend(parts, parts.Length - 1, create, path);
        if (parent.IsFile)
            throw new InvalidOperationException(
                $"'{Join(parts, parts.Length - 1)}' is a file, so '{path}' cannot be created");

        var name = parts[^1];

        if (!parent.Children.TryGetValue(name, out var node))
        {
            if (!create)
                throw new FileNotFoundException($"no such file: '{path}'", path);
            node = Node.NewFile(name);
            parent.Children[name] = node;
        }
        else if (!node.IsFile)
        {
            throw new InvalidOperationException($"'{path}' is a directory, not a file");
        }

        return node;
    }

    /// <summary>Is <paramref name="prefix"/> an ancestor of (or equal to) <paramref name="parts"/>?</summary>
    private static bool IsPrefix(string[] prefix, string[] parts)
    {
        if (prefix.Length > parts.Length)
            return false;

        for (int i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(prefix[i], parts[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string Join(string[] parts, int count) => "/" + string.Join('/', parts.Take(count));

    /// <summary>
    /// Glob match for ONE component. Greedy two-pointer with a backtrack point at
    /// the last '*': O(n * m) worst case, O(n + m) in practice, and no allocation.
    /// The DP table version is easier to prove but this one is easier to defend --
    /// on a single file name the inputs are tiny either way.
    /// </summary>
    private static bool GlobMatches(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, mark = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;          // remember where the '*' was, assume it eats nothing
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;        // backtrack: let that '*' swallow one more character
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }

    /// <summary>
    /// The OTHER reading of the path rules: resolve "." and ".." as navigation, and
    /// let a relative path be interpreted against <paramref name="workingDirectory"/>.
    /// Deliberately NOT part of the core walk -- the prompt's constraints allow dots
    /// inside names, so "..." is a legal file name and ".." would only mean "parent"
    /// if the interviewer says so. Ask before wiring this in.
    /// </summary>
    public static string Normalize(string path, string workingDirectory = "/")
    {
        var stack = new List<string>();

        if (!path.StartsWith('/'))
        {
            foreach (var part in workingDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
                stack.Add(part);
        }

        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);   // ".." at the root stays at the root
                continue;
            }
            stack.Add(part);
        }

        return "/" + string.Join('/', stack);
    }

    // ------------------------------------------------------------ the lock

    private T Read<T>(Func<T> body)
    {
        _lock.EnterReadLock();
        try { return body(); }
        finally { _lock.ExitReadLock(); }
    }

    private void Write(Action body)
    {
        _lock.EnterWriteLock();
        try { body(); }
        finally { _lock.ExitWriteLock(); }
    }

    private T Write<T>(Func<T> body)
    {
        _lock.EnterWriteLock();
        try { return body(); }
        finally { _lock.ExitWriteLock(); }
    }

    // ------------------------------------------------------------------ tests

    public static void Run()
    {
        Console.WriteLine("== the prompt's example ==");

        var fs = new InMemoryFileSystem();
        fs.Mkdir("/a/b/c");
        fs.AddContentToFile("/a/b/file.txt", "hello");

        var listing = fs.Ls("/a/b");
        Console.WriteLine($"  ls(\"/a/b\")                    -> [{string.Join(", ", listing.Select(n => $"\"{n}\""))}]");
        Console.WriteLine($"  readContentFromFile(...)      -> \"{fs.ReadContentFromFile("/a/b/file.txt")}\"");
        Console.WriteLine($"  matches expected: " +
                          $"{listing.SequenceEqual(new[] { "c", "file.txt" }) && fs.ReadContentFromFile("/a/b/file.txt") == "hello"}");
        Console.WriteLine("  note mkdir(\"/a/b/c\") created a, b AND c -- three nodes from one call.");
        Console.WriteLine(Indent(fs.Tree()));

        Console.WriteLine();
        Console.WriteLine("== the four rules that are easy to get wrong ==");

        Console.WriteLine($"  ls on a FILE returns the name:  [{string.Join(", ", fs.Ls("/a/b/file.txt"))}] (expect file.txt)");

        fs.AddContentToFile("/a/b/file.txt", " world");
        Console.WriteLine($"  addContentToFile APPENDS:       \"{fs.ReadContentFromFile("/a/b/file.txt")}\" (expect hello world)");

        fs.Mkdir("/a/b/c");
        Console.WriteLine($"  mkdir on an existing dir is a no-op: [{string.Join(", ", fs.Ls("/a/b"))}]");

        var empty = new InMemoryFileSystem();
        Console.WriteLine($"  ls(\"/\") on a fresh fs:          [{string.Join(", ", empty.Ls("/"))}] (expect empty, NOT an error)");

        empty.AddContentToFile("/x/y/deep.txt", "made the parents too");
        Console.WriteLine($"  addContentToFile creates parents: /x is a dir {empty.IsDirectory("/x")}, " +
                          $"/x/y is a dir {empty.IsDirectory("/x/y")}, /x/y/deep.txt is a file {empty.IsFile("/x/y/deep.txt")}");

        Console.WriteLine();
        Console.WriteLine("== ordinal vs culture ordering ==");

        var casing = new InMemoryFileSystem();
        foreach (var name in new[] { "banana", "Apple", "apple", "Banana", "_hidden", "42", "z1", "Z1" })
            casing.Mkdir("/mix/" + name);

        var ordinal = casing.Ls("/mix");
        var cultureSorted = ordinal.OrderBy(n => n).ToArray();          // the tempting one-liner

        Console.WriteLine($"  ordinal (correct):  [{string.Join(", ", ordinal)}]");
        Console.WriteLine($"  OrderBy(n => n):    [{string.Join(", ", cultureSorted)}]");
        Console.WriteLine($"  they disagree: {!ordinal.SequenceEqual(cultureSorted)}  <- same input, two different 'lexicographical' answers");
        Console.WriteLine("  Digits < uppercase < underscore < lowercase by character code. Culture");
        Console.WriteLine("  ordering ignores case first and would answer 'apple' before 'Banana'.");

        Console.WriteLine();
        Console.WriteLine("== error cases (the design half of the question) ==");

        var errors = new InMemoryFileSystem();
        errors.Mkdir("/dir");
        errors.AddContentToFile("/dir/f.txt", "data");

        foreach (var (label, act) in new (string, Action)[]
                 {
                     ("ls a missing directory",        () => errors.Ls("/nope")),
                     ("read a missing file",           () => errors.ReadContentFromFile("/dir/missing.txt")),
                     ("read a DIRECTORY as a file",    () => errors.ReadContentFromFile("/dir")),
                     ("write to a DIRECTORY",          () => errors.AddContentToFile("/dir", "x")),
                     ("mkdir under a FILE",            () => errors.Mkdir("/dir/f.txt/sub")),
                     ("relative path",                 () => errors.Ls("a/b")),
                     ("empty path",                    () => errors.Ls("")),
                     ("delete the root",               () => errors.Delete("/")),
                     ("move a dir into itself",        () => errors.Move("/dir", "/dir/inner/dir")),
                 })
        {
            try
            {
                act();
                Console.WriteLine($"  {label,-28} NOT rejected -- bug");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {label,-28} {ex.GetType().Name}: {ex.Message.Split(" (Parameter")[0]}");
            }
        }

        Console.WriteLine("  Every one of these is a judgement call the prompt does not make for you.");
        Console.WriteLine("  Returning an empty list instead would also be defensible -- silently is not.");

        Console.WriteLine();
        Console.WriteLine("== path forms that mean the same thing ==");

        var paths = new InMemoryFileSystem();
        paths.Mkdir("/a/b");
        Console.WriteLine($"  '/a//b' and '/a/b/' resolve too: {paths.IsDirectory("/a//b")} {paths.IsDirectory("/a/b/")}");
        Console.WriteLine($"  Normalize(\"/a/b/../c/./d\")     -> {Normalize("/a/b/../c/./d")}");
        Console.WriteLine($"  Normalize(\"../..\", \"/a/b\")      -> {Normalize("../..", "/a/b")}");
        Console.WriteLine($"  Normalize(\"/..\")               -> {Normalize("/..")}  (the root has no parent)");
        Console.WriteLine("  ^ kept OUT of the core walk: dots are legal name characters in this prompt.");

        Console.WriteLine();
        Console.WriteLine("== follow-ups: rm / mv / cp / du / glob ==");

        var ops = new InMemoryFileSystem();
        ops.Mkdir("/src/main");
        ops.AddContentToFile("/src/main/App.cs", "class App {}");
        ops.AddContentToFile("/src/main/Util.cs", "class Util {}");
        ops.AddContentToFile("/src/README.md", "# project");
        ops.AddContentToFile("/src/main/notes.txt", "todo");

        Console.WriteLine($"  du /src:                {ops.TotalSize("/src")} chars");
        Console.WriteLine($"  find /src/*/*.cs:       [{string.Join(", ", ops.Find("/src/*/*.cs"))}]");
        Console.WriteLine($"  find /src/*:            [{string.Join(", ", ops.Find("/src/*"))}]  <- one level only, '*' never crosses '/'");
        Console.WriteLine($"  find /src/main/?otes.*: [{string.Join(", ", ops.Find("/src/main/?otes.*"))}]  <- '?' is exactly one char");

        ops.Copy("/src", "/backup");
        ops.WriteContentToFile("/src/README.md", "# rewritten");
        Console.WriteLine($"  cp -r /src /backup, then overwrite the original:");
        Console.WriteLine($"    /src/README.md    = \"{ops.ReadContentFromFile("/src/README.md")}\"");
        Console.WriteLine($"    /backup/README.md = \"{ops.ReadContentFromFile("/backup/README.md")}\"  <- deep copy, no aliasing");

        ops.Move("/src/main", "/src/app");
        Console.WriteLine($"  mv /src/main /src/app:  [{string.Join(", ", ops.Ls("/src"))}]");

        Console.WriteLine($"  rm -r /backup:          {ops.Delete("/backup")}, again -> {ops.Delete("/backup")} (false, not an error)");
        Console.WriteLine(Indent(ops.Tree("/src")));

        Console.WriteLine();
        Console.WriteLine("== concurrency ==");

        var shared = new InMemoryFileSystem();
        Parallel.For(0, 64, i =>
        {
            shared.Mkdir($"/threads/{i % 8}/deep");
            shared.AddContentToFile($"/threads/{i % 8}/log.txt", "x");
            _ = shared.Ls("/threads");
            _ = shared.Exists($"/threads/{i % 8}/deep");
        });

        var buckets = shared.Ls("/threads");
        bool countsOk = buckets.Count == 8 &&
                        Enumerable.Range(0, 8).All(i => shared.ReadContentFromFile($"/threads/{i}/log.txt").Length == 8);
        Console.WriteLine($"  64 parallel mkdir+append+ls over 8 directories, nothing lost or duplicated: {countsOk}");
        Console.WriteLine("  A single reader/writer lock makes that true. It also serializes every write in");
        Console.WriteLine("  the whole tree, which is fine here and wrong for a real fs -- see the notes.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-check against a flat-map model ==");

        // The model stores paths as strings with no tree at all. If the two agree
        // on ls/read after thousands of random mutations, the tree walk is right.
        var rng = new Random(20260810);
        bool lsMatches = true, readMatches = true, sortedAlways = true, existsMatches = true;

        for (int trial = 0; trial < 400; trial++)
        {
            var real = new InMemoryFileSystem();
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };
            var touched = new List<string> { "/" };

            for (int op = 0; op < 40; op++)
            {
                var path = RandomPath(rng);

                if (rng.Next(2) == 0)
                {
                    // mkdir: skip when a FILE already occupies the path or blocks it.
                    if (Prefixes(path).Any(files.ContainsKey))
                        continue;
                    real.Mkdir(path);
                    foreach (var prefix in Prefixes(path))
                        dirs.Add(prefix);
                }
                else
                {
                    if (dirs.Contains(path) || Prefixes(Parent(path)).Any(files.ContainsKey))
                        continue;
                    var chunk = ((char)('a' + rng.Next(26))).ToString();
                    real.AddContentToFile(path, chunk);
                    files[path] = files.TryGetValue(path, out var existing) ? existing + chunk : chunk;
                    foreach (var prefix in Prefixes(Parent(path)))
                        dirs.Add(prefix);
                }

                touched.Add(path);
            }

            foreach (var path in touched.Distinct())
            {
                bool shouldExist = dirs.Contains(path) || files.ContainsKey(path);
                existsMatches &= real.Exists(path) == shouldExist;
                if (!shouldExist)
                    continue;

                // ls: a file lists itself; a directory lists its direct children.
                var expected = files.ContainsKey(path)
                    ? new[] { path[(path.LastIndexOf('/') + 1)..] }
                    : dirs.Concat(files.Keys)
                        .Where(p => p != "/" && Parent(p) == path)
                        .Select(p => p[(p.LastIndexOf('/') + 1)..])
                        .Distinct()
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToArray();

                var actual = real.Ls(path);
                lsMatches &= actual.SequenceEqual(expected);
                sortedAlways &= actual.SequenceEqual(actual.OrderBy(n => n, StringComparer.Ordinal));

                if (files.TryGetValue(path, out var content))
                    readMatches &= real.ReadContentFromFile(path) == content;
            }
        }

        Console.WriteLine($"  400 trials x 40 random mkdir/append operations");
        Console.WriteLine($"    ls agrees with the model:              {lsMatches}");
        Console.WriteLine($"    read agrees (appends accumulated):     {readMatches}");
        Console.WriteLine($"    every listing ordinally sorted:        {sortedAlways}");
        Console.WriteLine($"    exists agrees for files and dirs:      {existsMatches}");

        // Copy/Move must preserve the flattened contents of the whole tree.
        bool moveIsContentPreserving = true, copyDoublesSize = true;
        for (int trial = 0; trial < 200; trial++)
        {
            var tree = new InMemoryFileSystem();
            for (int i = 0; i < 12; i++)
                tree.AddContentToFile($"/{"abc"[rng.Next(3)]}/{"fgh"[rng.Next(3)]}.txt", "z");

            int size = tree.TotalSize();

            tree.Mkdir("/holding");
            foreach (var top in tree.Find("/*").Where(p => p != "/holding").ToList())
                tree.Move(top, "/holding" + top);

            moveIsContentPreserving &= tree.TotalSize() == size && tree.TotalSize("/holding") == size;

            tree.Copy("/holding", "/holding2");
            copyDoublesSize &= tree.TotalSize() == 2 * size;
        }

        Console.WriteLine($"  200 trials of bulk mv into a new root: total content preserved: {moveIsContentPreserving}");
        Console.WriteLine($"  cp -r of the whole tree doubles du:                             {copyDoublesSize}");

        bool globMatchesRegex = true;
        for (int trial = 0; trial < 5000; trial++)
        {
            string text = RandomWord(rng, 6);
            string pattern = new string(RandomWord(rng, 4).Select(c => rng.Next(4) switch
            {
                0 => '*',
                1 => '?',
                _ => c,
            }).ToArray());

            var regex = "^" + string.Concat(pattern.Select(c => c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => System.Text.RegularExpressions.Regex.Escape(c.ToString()),
            })) + "$";

            globMatchesRegex &= GlobMatches(pattern, text)
                == System.Text.RegularExpressions.Regex.IsMatch(text, regex);
        }

        Console.WriteLine($"  5,000 random globs agree with the equivalent regex:             {globMatchesRegex}");
    }

    private static string RandomPath(Random rng)
    {
        // A tiny alphabet on purpose: collisions are what exercise "already exists",
        // "a file is in the way" and the append path.
        int depth = rng.Next(1, 4);
        var parts = Enumerable.Range(0, depth).Select(_ => "abc"[rng.Next(3)].ToString());
        return "/" + string.Join('/', parts);
    }

    private static string RandomWord(Random rng, int maxLength)
        => new(Enumerable.Range(0, rng.Next(0, maxLength + 1)).Select(_ => "ab"[rng.Next(2)]).ToArray());

    private static IEnumerable<string> Prefixes(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i <= parts.Length; i++)
            yield return "/" + string.Join('/', parts.Take(i));
    }

    private static string Parent(string path)
    {
        int cut = path.LastIndexOf('/');
        return cut <= 0 ? "/" : path[..cut];
    }

    private static string Indent(string text, int spaces = 2)
    {
        var pad = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => pad + line));
    }
}

// ---- Notes for the follow-up questions ----
//
// "How would you store the content of a large file?"
//     Not one string. Chop it into fixed-size blocks (4KB is the classic) held in a
//     list, so an append touches one block instead of copying the file, and a read
//     of bytes [i, j) touches only ceil((j-i)/B) blocks. That is also the door into
//     the real answer -- an inode holding block POINTERS -- which is what makes
//     sparse files, copy-on-write clones and partial reads possible at all.
//
// "Now make it persistent."
//     Append every mutation to a write-ahead log before applying it, and snapshot
//     the tree periodically; recovery = load the last snapshot, replay the tail of
//     the log. The tree above is already the in-memory index, which is precisely
//     how a journaling fs (and every LSM database) is laid out.
//
// "One lock for the whole tree is a bottleneck."
//     True. The next step is a lock per directory node, taken in path order
//     (root -> leaf) so lock ordering is total and deadlock is impossible by
//     construction -- this is hand-over-hand / lock coupling. Beyond that, make the
//     children maps concurrent and use RCU-style publication so readers never lock
//     at all, which is what Linux's dcache does. Note what gets harder: `mv` spans
//     two directories, so it must take both locks (in order) and is where every
//     fine-grained scheme grows a special case.
//
// "Add hard links / symlinks."
//     Hard links: split the node into a `name -> inode` map plus refcounted inodes;
//     the tree stops being a tree and becomes a DAG, and delete becomes "decrement,
//     free at zero". Symlinks: a third node kind holding a path, resolved during
//     the walk -- with a hop limit (Linux uses 40) because symlink loops are real
//     and an unbounded walk hangs the process.
//
// "Support search by name anywhere in the tree."
//     Find above is O(size of the matched subtree). If that query is hot, maintain
//     a side index name -> list of nodes, updated on every mutation; it costs
//     memory and makes mv/rm more expensive, so only do it if the read/write ratio
//     justifies it. For prefix search over names, a trie over the index keys.
//
// "What about permissions, quotas, timestamps?"
//     All are per-node metadata, so the tree does not change shape -- but the WALK
//     does: a permission check happens at every component (you need +x on each
//     directory to traverse it), which is why an unreadable directory hides
//     everything under it. Quotas need a size counter propagated to ancestors on
//     every write, so `du` becomes O(1) and writes become O(depth).
//
// "Why not just a flat Dictionary<string, string> of full paths?"
//     Reads become O(1), so it is genuinely better for a key-value store. It falls
//     apart on the operations that make this a FILE SYSTEM: ls has to scan every
//     key, and mv of a directory has to rewrite every descendant's key. The tree
//     pays O(p) on lookup to make those two O(children) and O(1). That trade IS the
//     answer to "why a tree", and the randomized test above uses the flat map as
//     the model precisely because it is the obvious-but-wrong design.
