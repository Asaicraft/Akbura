using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.Collections;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Akbura.AotTest;

[AkburaSource("ProfileWithTasks.akbura", AssemblyName = "Akbura.AotTest")]
internal sealed class ProfileWithTasks : AkburaComponent
{
    // ---------- inject ----------
    private readonly ILogger<ProfileWithTasks> _log;

    // ---------- param ----------
    [AkburaParam("UserId", Line = 3, Character = 1)]
    private readonly int __param_UserId;

    // ---------- state ----------
    [AkburaState("user", DefaultValue = null)]
    private object? __state_user = null;

    [AkburaState("stats")]
    private Stats __state_stats = new Stats { Open = 0, Done = 0 };

    [AkburaState("tasks")]
    private ReactList<TaskDto> __state_tasks = new();

    [AkburaState("isSaving")]
    private bool __state_isSaving = false;

    [AkburaState("showForm")]
    private bool __state_showForm = false;

    [AkburaState("form")]
    private FormDto __state_form = new FormDto { Title = "", Desc = "", Urgent = false };

    [AkburaState("isValid")]
    private bool __state_isValid = false;

    [AkburaState("search")]
    private string __state_search = "";

    [AkburaState("visible")]
    private ReactList<TaskDto> __state_visible = new();

    // ---------- infra ----------
    private __ProfileWithTasks_view_ __view__0;
    private readonly CancellationTokenSource __eff_initialLoadCts = new();
    private readonly CancellationTokenSource __eff_searchCts = new();

    // Эффекты: наблюдаемые ключи (для компилятора — таблица зависимостей)
    // - Validation: depends on form.Title
    // - Initial load: depends on UserId (param) and cancellation
    // - Search: depends on search, tasks and cancellation

    public ProfileWithTasks(ILogger<ProfileWithTasks> log, int userId)
    {
        _log = log;
        __param_UserId = userId;
        __view__0 = new __ProfileWithTasks_view_(this);
    }

    public override ViewContainer Update()
    {
        // Первичный mount — статическая структура, данные обновляются через UpdateView
        return new(__view__0, isStaticMountComponent: false);
    }

    public override void OnMounted()
    {
        // Триггерим эффекты при первом маунте
        __Run_Validation_Effect();
        _ = __Run_InitialLoad_Effect(__eff_initialLoadCts.Token);
        _ = __Run_Search_Effect(__eff_searchCts.Token);
    }

    public override void OnUnmounted()
    {
        // Аккуратно гасим эффекты
        try { __eff_initialLoadCts.Cancel(); } catch { }
        try { __eff_searchCts.Cancel(); } catch { }
    }

    // ============================================================
    //                  EFFECTS (сгенерированные)
    // ============================================================

    // --- Validation: useEffect(form.Title) ---
    private void __Run_Validation_Effect()
    {
        var ok = !string.IsNullOrWhiteSpace(__state_form?.Title?.Trim());
        if (__state_isValid != ok)
        {
            __state_isValid = ok;
            InvalidState(States.From("isValid"));
        }
    }

    // --- Initial load: useEffect($cancel, UserId) + suppress ---
    private async Task __Run_InitialLoad_Effect(CancellationToken ct)
    {
        // Busy флаг можно выставлять снаружи; здесь просто строго по тексту finally
        try
        {
            await SuppressAsync(onCancel: SuppressStrategy.Discard, onError: SuppressStrategy.Discard, async () =>
            {
                var uTask = api.LoadUser(__param_UserId, ct);
                var tTask = api.LoadTasks(__param_UserId, ct);

                var s = new Stats { Open = 0, Done = 0 };

                var u = await uTask.ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                var ts = await tTask.ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                foreach (var it in ts)
                {
                    if (it.Done) s = new Stats { Open = s.Open, Done = s.Done + 1 };
                    else s = new Stats { Open = s.Open + 1, Done = s.Done };
                }

                __state_user = u;
                __state_stats = s;

                __state_tasks.ReplaceAll(ts);
                __state_visible.ReplaceAll(ts);

                InvalidState(States.From("user", "stats", "tasks", "visible"));
            });
        }
        catch (OperationCanceledException)
        {
            _log?.LogInformation("Load cancelled for UserId={UserId}", __param_UserId);
        }
        catch (Exception ex)
        {
            // По спецификации — onError=discard, но лог оставим
            _log?.LogError(ex, "Initial load failed");
        }
        finally
        {
            // finally{} из DSL
            App.Busy = false;
        }
    }

    // --- Search: useEffect($cancel, search, tasks) ---
    private async Task __Run_Search_Effect(CancellationToken ct)
    {
        try
        {
            __state_visible.ReplaceAll(__state_tasks.ToList());
            InvalidState(States.From("visible"));

            await Delay(250, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            var q = __state_search?.Trim()?.ToLowerInvariant() ?? "";
            if (q.Length == 0) return;

            var filtered = __state_tasks.Where(t => SafeLower(t.Title).Contains(q)).ToList();
            __state_visible.ReplaceAll(filtered);
            InvalidState(States.From("visible"));
        }
        catch (OperationCanceledException)
        {
            // норм
        }
    }

    // ============================================================
    //                  EVENTS / COMMANDS (сгенерированные)
    // ============================================================

    // --- SaveNew(): async + suppress ---
    [AkburaEvent("Click", Line = 120, Character = 11)]
    private async void __event_SaveNew()
    {
        if (!__state_isValid || __state_isSaving) return;

        __state_isSaving = true;
        InvalidState(States.From("isSaving"));

        try
        {
            await SuppressAsync(onCancel: SuppressStrategy.Discard, onError: SuppressStrategy.Discard, async () =>
            {
                var dto = new CreateTaskDto
                {
                    Title = __state_form.Title,
                    Desc = __state_form.Desc,
                    Urgent = __state_form.Urgent,
                    Done = false
                };

                var created = await api.CreateTask(__param_UserId, dto).ConfigureAwait(false);

                __state_tasks.Prepend(created);

                var q = __state_search?.Trim()?.ToLowerInvariant() ?? "";
                if (q.Length == 0 || SafeLower(created.Title).Contains(q))
                    __state_visible.Prepend(created);

                __state_form = new FormDto { Title = "", Desc = "", Urgent = false };
                __state_showForm = false;

                InvalidState(States.From("tasks", "visible", "form", "showForm"));
            });
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "CreateTask failed");
        }
        finally
        {
            __state_isSaving = false;
            InvalidState(States.From("isSaving"));
        }
    }

    // --- ToggleDone(item): async + suppress optimistic ---
    [AkburaEvent("Toggle", Line = 143, Character = 21)]
    private async void __event_ToggleDone(TaskDto item)
    {
        await SuppressAsync(onCancel: SuppressStrategy.Discard, onError: SuppressStrategy.Commit, async () =>
        {
            var idx = __state_tasks.FindIndex(t => t.Id == item.Id);
            var vIdx = __state_visible.FindIndex(t => t.Id == item.Id);

            if (idx >= 0)
            {
                var changed = new TaskDto
                {
                    Id = item.Id,
                    Title = item.Title,
                    Desc = item.Desc,
                    Urgent = item.Urgent,
                    Done = !item.Done
                };
                __state_tasks.ReplaceAt(idx, changed);
                if (vIdx >= 0) __state_visible.ReplaceAt(vIdx, changed);
                InvalidState(States.From("tasks", "visible"));
            }

            await api.UpdateDone(item.Id, !item.Done).ConfigureAwait(false);
        });
    }

    // --- OpenForm() ---
    [AkburaEvent("Click", Line = 170, Character = 36)]
    private void __event_OpenForm()
    {
        __state_form = new FormDto { Title = "", Desc = "", Urgent = false };
        __state_showForm = true;
        InvalidState(States.From("form", "showForm"));
    }

    // ============================================================
    //         REACTIONS НА ИЗМЕНЕНИЯ STATE/INPUT (bind:Text и т.д.)
    // ============================================================

    // search TextBox -> bind:Text={search}
    private void __onSearchChanged(string? v)
    {
        var nv = v ?? "";
        if (!StringEquals(__state_search, nv))
        {
            __state_search = nv;
            InvalidState(States.From("search"));

            // перезапускаем search-effect
            try { __eff_searchCts.Cancel(); } catch { }
            // новый токен
            var cts = new CancellationTokenSource();
            typeof(ProfileWithTasks).GetField("__eff_searchCts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(this, cts);
            _ = __Run_Search_Effect(cts.Token);
        }
    }

    // form.Title
    private void __onFormTitleChanged(string? v)
    {
        var updated = __state_form with { Title = v ?? "" };
        if (!FormEquals(__state_form, updated))
        {
            __state_form = updated;
            InvalidState(States.From("form"));
            __Run_Validation_Effect();
        }
    }

    // form.Desc
    private void __onFormDescChanged(string? v)
    {
        var updated = __state_form with { Desc = v ?? "" };
        if (!FormEquals(__state_form, updated))
        {
            __state_form = updated;
            InvalidState(States.From("form"));
        }
    }

    // form.Urgent
    private void __onFormUrgentChanged(bool v)
    {
        var updated = __state_form with { Urgent = v };
        if (!FormEquals(__state_form, updated))
        {
            __state_form = updated;
            InvalidState(States.From("form"));
        }
    }

    // ============================================================
    //                         VIEW
    // ============================================================

    private class __ProfileWithTasks_view_ : AvaloniaAkburaView
    {
        private readonly ProfileWithTasks _cmp;

        // корневые и важные контролы
        private Stack _root;

        // header
        private TextBox _searchBox;

        // profile card
        private Grid _profileCard;
        private Avatar _avatar;
        private Text _userName;
        private Text _userEmail;
        private Badge _openBadge;
        private Badge _doneBadge;

        // list header
        private Button _btnNew;

        // list container
        private ItemsHost<TaskDto> _listHost;  // упрощённая абстракция For/react

        // modal
        private Modal? _modal;
        private TextBox _titleBox;
        private TextArea _descArea;
        private CheckBox _urgentCheck;
        private Button _btnCreate;
        private Button _btnCancel;
        private Text _validationOk;
        private Text _validationErr;

        public __ProfileWithTasks_view_(ProfileWithTasks cmp)
        {
            _cmp = cmp;
        }

        public override void MountToTree(StyledElement? parent)
        {
            // ---- build static tree (упрощённо) ----
            _root = new Stack { Class = "page", Gap = 16 };

            // Header row
            var header = new Row { Align = Align.Center, Gap = 12 };
            header.Children.Add(new Text { Class = "h1", Content = "User dashboard" });
            header.Children.Add(new Spacer());
            _searchBox = new TextBox { Class = "search", Placeholder = "Search tasks..." };
            _searchBox.Text = _cmp.__state_search ?? "";
            _searchBox.TextChanged += (s, e) => _cmp.__onSearchChanged(_searchBox.Text);
            header.Children.Add(_searchBox);
            _root.Children.Add(header);

            // Profile area (Await)
            // Fallback skeleton и основная карточка
            _profileCard = BuildProfileCard();
            UpdateProfileCard(); // заполнит значениями либо оставит пустыми
            _root.Children.Add(_profileCard);

            // List header
            var listHeader = new Row { Align = Align.Center };
            listHeader.Children.Add(new Text { Class = "section", Content = "Tasks" });
            listHeader.Children.Add(new Spacer());
            _btnNew = new Button { Class = "primary", Content = "New task" };
            _btnNew.Click += _cmp.__event_OpenForm;
            listHeader.Children.Add(_btnNew);
            _root.Children.Add(listHeader);

            // For react={visible}
            _listHost = new ItemsHost<TaskDto>(
                keySelector: t => t.Id,
                onCreate: CreateTaskRow,
                onUpdate: UpdateTaskRow
            );
            _listHost.ReplaceAll(_cmp.__state_visible.ToList());
            _root.Children.Add(_listHost.Element);

            // Empty state (Await When={visible.Count > 0} …)
            // Упростим: сам ItemsHost прячет/показывает EmptyState
            _listHost.EmptyFallback = new EmptyState { Icon = "inbox", Title = "No tasks", Subtitle = "Try creating one" };

            // Modal (Await When={showForm})
            // Создаём сразу; видимость управляется UpdateView
            _modal = new Modal { Title = "New task", IsOpen = _cmp.__state_showForm };
            _modal.OnClose += (s, e) => { _cmp.__state_showForm = false; _cmp.InvalidState(States.From("showForm")); };

            var formStack = new Stack { Gap = 12 };
            _titleBox = new TextBox { Label = "Title", Text = _cmp.__state_form.Title };
            _titleBox.TextChanged += (s, e) => _cmp.__onFormTitleChanged(_titleBox.Text);
            formStack.Children.Add(_titleBox);

            _descArea = new TextArea { Label = "Description", Text = _cmp.__state_form.Desc };
            _descArea.TextChanged += (s, e) => _cmp.__onFormDescChanged(_descArea.Text);
            formStack.Children.Add(_descArea);

            var urgentRow = new Row { Gap = 8 };
            _urgentCheck = new CheckBox { IsChecked = _cmp.__state_form.Urgent };
            _urgentCheck.CheckedChanged += (s, e) => _cmp.__onFormUrgentChanged(_urgentCheck.IsChecked);
            urgentRow.Children.Add(_urgentCheck);
            urgentRow.Children.Add(new Text { Content = "Urgent" });
            formStack.Children.Add(urgentRow);

            // validation swap
            _validationOk = new Text { Class = "ok", Content = "Looks good" };
            _validationErr = new Text { Class = "error", Content = "Title is required" };
            formStack.Children.Add(_validationOk);   // видимость переключим в UpdateView
            formStack.Children.Add(_validationErr);

            var btnRow = new Row { Gap = 8 };
            _btnCreate = new Button { Class = "primary", Content = _cmp.__state_isSaving ? "Saving..." : "Create" };
            _btnCreate.Enabled = _cmp.__state_isValid && !_cmp.__state_isSaving;
            _btnCreate.Click += _cmp.__event_SaveNew;
            btnRow.Children.Add(_btnCreate);

            _btnCancel = new Button { Class = "ghost", Content = "Cancel" };
            _btnCancel.Click += (s, e) => { _cmp.__state_showForm = false; _cmp.InvalidState(States.From("showForm")); };
            btnRow.Children.Add(_btnCancel);

            formStack.Children.Add(btnRow);

            _modal.Content = formStack;
            _root.Children.Add(_modal);

            parent!.Children.Add(_root);
        }

        public override void UpdateView(States s)
        {
            // user/stats
            if (s.Contains("user") || s.Contains("stats"))
            {
                UpdateProfileCard();
            }

            // search — (в UI уже сразу применён в эффекте), тут ничего

            // visible/tasks — обновляем список
            if (s.Contains("visible") || s.Contains("tasks"))
            {
                _listHost.ReplaceAll(_cmp.__state_visible.ToList());
            }

            // form/isValid/isSaving/showForm — модалка и кнопки
            if (s.Contains("form") || s.Contains("isValid") || s.Contains("isSaving") || s.Contains("showForm"))
            {
                if (_modal is not null)
                {
                    _modal.IsOpen = _cmp.__state_showForm;

                    _titleBox.Text = _cmp.__state_form.Title;
                    _descArea.Text = _cmp.__state_form.Desc;
                    _urgentCheck.IsChecked = _cmp.__state_form.Urgent;

                    _validationOk.IsVisible = _cmp.__state_isValid;
                    _validationErr.IsVisible = !_cmp.__state_isValid;

                    _btnCreate.Content = _cmp.__state_isSaving ? "Saving..." : "Create";
                    _btnCreate.Enabled = _cmp.__state_isValid && !_cmp.__state_isSaving;
                }
            }
        }

        public override void UnmountFromTree()
        {
            // Снимаем события
            _btnNew.Click -= _cmp.__event_OpenForm;
            _btnCreate.Click -= _cmp.__event_SaveNew;

            _searchBox.TextChanged -= (s, e) => _cmp.__onSearchChanged(_searchBox.Text);
            _titleBox.TextChanged -= (s, e) => _cmp.__onFormTitleChanged(_titleBox.Text);
            _descArea.TextChanged -= (s, e) => _cmp.__onFormDescChanged(_descArea.Text);
            _urgentCheck.CheckedChanged -= (s, e) => _cmp.__onFormUrgentChanged(_urgentCheck.IsChecked);

            // Размонтируем
            _root.Parent.Children.Remove(_root);
        }

        // ---- helpers ----

        private Grid BuildProfileCard()
        {
            var g = new Grid { Columns = "96, *", Class = "card" };

            _avatar = new Avatar { Size = 96 };
            g.Children.Add(_avatar);

            var s = new Stack { Gap = 10 };
            _userName = new Text { Class = "title" };
            _userEmail = new Text { Class = "muted" };
            var badges = new Row { Gap = 8 };
            _openBadge = new Badge();
            _doneBadge = new Badge();
            badges.Children.Add(_openBadge);
            badges.Children.Add(_doneBadge);

            s.Children.Add(_userName);
            s.Children.Add(_userEmail);
            s.Children.Add(badges);

            g.Children.Add(s);
            return g;
        }

        private void UpdateProfileCard()
        {
            if (_cmp.__state_user is null)
            {
                // Fallback skeleton (упрощённо скрываем карточку)
                _profileCard.IsVisible = false;
                return;
            }

            _profileCard.IsVisible = true;

            dynamic u = _cmp.__state_user;
            _avatar.Src = (string?)u.AvatarUrl ?? "";
            _userName.Content = (string?)u.Name ?? "";
            _userEmail.Content = (string?)u.Email ?? "";
            _openBadge.Content = $"Open: {_cmp.__state_stats.Open}";
            _doneBadge.Content = $"Done: {_cmp.__state_stats.Done}";
        }

        private Control CreateTaskRow(TaskDto it)
        {
            var grid = new Grid { Columns = "24, *, auto, auto", Class = "row" };

            var cb = new CheckBox { IsChecked = it.Done };
            cb.CheckedChanged += (s, e) => _cmp.__event_ToggleDone(it);

            var left = new Stack();
            var title = new Text { Class = "task", Content = it.Title };
            var id = new Text { Class = "muted", Content = $"#{it.Id}" };
            left.Children.Add(title);
            left.Children.Add(id);

            var badge = new Badge { Class = it.Urgent ? "warn" : "ok", Content = it.Urgent ? "urgent" : "normal" };

            var edit = new Button { Class = "ghost", Content = "Edit" };
            // заглушка под модалку редактирования

            grid.Children.Add(cb);
            grid.Children.Add(left);
            grid.Children.Add(badge);
            grid.Children.Add(edit);

            return grid;
        }

        private void UpdateTaskRow(Control row, TaskDto it)
        {
            var grid = (Grid)row;
            var cb = (CheckBox)grid.Children[0];
            var left = (Stack)grid.Children[1];
            var title = (Text)left.Children[0];
            var id = (Text)left.Children[1];
            var badge = (Badge)grid.Children[2];

            cb.IsChecked = it.Done;
            title.Content = it.Title;
            id.Content = $"#{it.Id}";
            badge.Class = it.Urgent ? "warn" : "ok";
            badge.Content = it.Urgent ? "urgent" : "normal";
        }
    }

    // ============================================================
    //                    ВСПОМОГАТЕЛЬНЫЕ ТИПЫ/АПИ
    // ============================================================

    private static string SafeLower(string? s) => (s ?? "").ToLowerInvariant();
    private static bool StringEquals(string a, string b) => StringComparer.Ordinal.Equals(a, b);
    private static Task Delay(int ms, CancellationToken ct) => Task.Delay(ms, ct);

    private static bool FormEquals(FormDto a, FormDto b)
        => a.Title == b.Title && a.Desc == b.Desc && a.Urgent == b.Urgent;

    private Task SuppressAsync(SuppressStrategy onCancel, SuppressStrategy onError, Func<Task> body)
        => SuppressTransaction.RunAsync(onCancel, onError, body);

    // ===== DTO/State containers =====
    private sealed record FormDto
    {
        public string Title { get; init; } = "";
        public string Desc { get; init; } = "";
        public bool Urgent { get; init; }
    }

    private sealed class Stats
    {
        public int Open { get; init; }
        public int Done { get; init; }
    }

    private sealed class ReactList<T> : List<T>
    {
        public void ReplaceAll(IEnumerable<T> src)
        {
            this.Clear();
            this.AddRange(src);
        }
        public void Prepend(T item) => this.Insert(0, item);
        public int FindIndex(Func<T, bool> pred)
        {
            for (var i = 0; i < Count; i++)
                if (pred(this[i])) return i;
            return -1;
        }
        public void ReplaceAt(int idx, T value) => this[idx] = value;
    }

    private sealed class TaskDto
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public string Desc { get; init; } = "";
        public bool Urgent { get; init; }
        public bool Done { get; init; }
    }

    private sealed class CreateTaskDto
    {
        public string Title { get; init; } = "";
        public string Desc { get; init; } = "";
        public bool Urgent { get; init; }
        public bool Done { get; init; }
    }

    private enum SuppressStrategy { Discard, Commit }

    private static class SuppressTransaction
    {
        public static async Task RunAsync(SuppressStrategy onCancel, SuppressStrategy onError, Func<Task> body)
        {
            try
            {
                await body().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (onCancel == SuppressStrategy.Commit) throw;
            }
            catch
            {
                if (onError == SuppressStrategy.Commit) throw;
            }
        }
    }

    // Заглушка внешнего API — в реале подставит проект
    private static class api
    {
        public static Task<object> LoadUser(int userId, CancellationToken ct) => Task.FromResult<object>(new { Name = "User", Email = "user@mail", AvatarUrl = "" });
        public static Task<List<TaskDto>> LoadTasks(int userId, CancellationToken ct) => Task.FromResult(new List<TaskDto>());
        public static Task<TaskDto> CreateTask(int userId, CreateTaskDto dto) => Task.FromResult(new TaskDto { Id = new Random().Next(1, 9999), Title = dto.Title, Desc = dto.Desc, Urgent = dto.Urgent, Done = false });
        public static Task UpdateDone(int id, bool done) => Task.CompletedTask;
    }
}
