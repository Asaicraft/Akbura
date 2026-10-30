# Component probe template cache — FeatureGallery

Исходный коммит: `13fc40e32e9de93607314044993e5b8a6aec6d86`. Ветка: `perf/micro-optimizations`.

## Решение

Выбран `ConcurrentDictionary<string, CSharp.CompilationUnitSyntax>(StringComparer.Ordinal)` с исходной публикацией `template = _componentTemplates.GetOrAdd(fallbackTypeName, template)` после построения. Production diff — две строки. В текущих callers всего 11 фиксированных fallback names; словарь одного документа не получает неограниченный поток пользовательских ключей.

У каждого bounded варианта обнаружены повторные построения на **реальном порядке пяти имён FeatureGallery** в независимых процессах. У Dictionary их нет. При обычных попаданиях время близко; небольшая экономия cache memory не оправдывает зависимость задержки от seed. Это выбор стабильности смешанных semantic requests. Измерения не доказывают ускорение всего генератора.

## Область и жизненный цикл

Кеш принадлежит `CSharpProbeContext`, который создаётся `BindingSession` одного `AkburaSemanticModel`. Ключ — `fallbackTypeName`, comparer — Ordinal. `CreateComponentProbeCompilationUnitCore` разрешает тип компонента и создаёт неизменяемый шаблон с namespace, extern aliases и возможным base type. Пустые members/usings передаются в фабрику; конкретные members/usings присоединяются к отдельному результату. Промахи и параллельные фабрики безопасны семантически, но требуют повторного построения синтаксиса и разрешения типа.

Все вызовы проверены в CSharpProbeBinder, CSharpProbeBuilder, UseHooks, AkburaSemanticModel и CSharpReferences. Полный набор: `__AkburaStatementProbe`, `__AkburaTypeProbe`, `__AkburaReturnTypeProbe`, `__AkburaDeclarationNameProbe`, `__AkburaUsingProbe`, `__AkburaCommandProbe`, `__AkburaProbe`, `__AkburaComponentMethodProbe`, `__AkburaUseHookProbe`, `__AkburaUseHookCompletionProbe`, `__AkburaSemanticProbe`.

Другие кеши, TryGetValue в AkburaModuleTypeResolver, инфраструктура ConcurrentCache, PooledDelegates, Runtime, Workspaces, LanguageServer и Furioso не изменены.

## Методика

Release / .NET 10.0.12, workstation GC, `DOTNET_TieredCompilation=0`. Один процесс выполняет полный FeatureGallery опыт; один дополнительный процесс повторяет cold generation в обратном порядке вариантов; ещё 8 независимых процессов повторяют focused workloads. В каждом процессе пять независимых DLL загружаются последовательно через collectible AssemblyLoadContext. Roslyn compilation, references, SourceText и comparer runtime общие; поэтому у всех пяти вариантов один hash seed. Порядок вариантов поворачивается между focused процессами; cold control выполняется в обратном порядке.

MSBuildWorkspace загружает настоящий проект один раз на процесс; project load, references, подготовка fixtures, прогрев, forced GC и проверка выходов исключены из таймера. Используется только BlackSilence. E2E: 1 warmup + 3 samples; холодный сценарий каждый раз создаёт свежий driver/context. Инкрементальные сценарии продвигают driver и чередуют forward/reverse edit (после первого warmup: reverse, forward, reverse). Создание driver исключено из ColdGeneration. Замеры — весь RunGenerators/GetRunResult и контроль ошибок, включая worker allocations.

Focused fixture: реальный semantic model CommandDemo с полным AkburaCompilation галереи; реальный binder и восстановленные из trace member/using syntax. ObservedOrder воспроизводит все реальные members/usings каждого запроса. В прочих распределениях один фиксированный реальный набор members/usings из первого запроса изолирует влияние ключей. Не наблюдавшиеся имена и constant-hash stress — дополнительные случаи, а не основной материал выбора.

По каждому focused случаю: 1 warmup + 3 измерения; холодный проход повторяется 64 раза с новым кешем, тёплый — минимум 16384 запросов (ObservedOrder: 18944). Холодный focused замер включает benchmark-only reflection для подмены нового кеша; это не чистое время production constructor. В тёплом таймере reflection/fixture construction нет. Gen0 — process-wide delta; allocations — GC.GetTotalAllocatedBytes(true).

Factory/hit counters собираются отдельными инструментированными DLL, не участвующими в тайминге. Для последовательных focused workloads точные direct-map расчёты сверены с фактическими счётчиками в дополнительных процессах. Параллельные счётчики получены отдельно и могут зависеть от влияния trace lock на scheduling. Параллельный таймер включает ToFullString и точное сравнение выходов, поэтому его нельзя сравнивать напрямую с serial ns/op.

SHA256/MVID Generator, BlackSilence, benchmark, входы, references, runtime, samples, buckets и profile сохранены в JSON. В каждом процессе все пять вариантов используют одинаковый benchmark SHA256. Между focused и полным опытом добавлены только отчётные поля/проверка результатов и обратный порядок. Тела timed focused операций не менялись. Первоначальная проверка двух fresh runs выявила перестановку Roslyn CS8019: 39 source texts и состав 547 compiler diagnostics совпадали. После канонизации полная матрица повторена. Затем full-0 timing остановлен из-за заполнения C: при записи snapshot: COMPLETE tracing всех пяти вариантов сохранён и используется отдельно; частичные timing samples full-0 исключены. Артефакты перенесены на D: с SHA256-проверкой. Финальная полная timing-матрица full-1 выполнена заново целиком, с другим seed, без tracing. Дополнительный cold control имеет собственные paired profile/timing.

Таблицы actual request/miss/retained (A) относятся к profiling seed full-0; времена E2E (C) — к отдельному uninstrumented процессу full-1 и cold control. Не сопоставляйте напрямую A как factory counts конкретного timed E2E run: seeds различаются. Focused factory counts в primary таблице рассчитаны точно для каждого timing seed, и расчёт проверен отдельными paired instrumented runs восьми процессов и cold control.

## Реальные обращения FeatureGallery

| Имя | Запросы (cold) | Dictionary miss | Cache16 miss | Cache32 miss | Cache64 miss | Cache128 miss |
|---|---:|---:|---:|---:|---:|---:|
| __AkburaStatementProbe | 9 | 7 | 9 | 9 | 9 | 7 |
| __AkburaTypeProbe | 0 | 0 | 0 | 0 | 0 | 0 |
| __AkburaReturnTypeProbe | 0 | 0 | 0 | 0 | 0 | 0 |
| __AkburaDeclarationNameProbe | 0 | 0 | 0 | 0 | 0 | 0 |
| __AkburaUsingProbe | 0 | 0 | 0 | 0 | 0 | 0 |
| __AkburaCommandProbe | 0 | 0 | 0 | 0 | 0 | 0 |
| __AkburaProbe | 2981 | 33 | 33 | 33 | 33 | 33 |
| __AkburaComponentMethodProbe | 46 | 5 | 5 | 5 | 5 | 5 |
| __AkburaUseHookProbe | 79 | 7 | 10 | 10 | 10 | 7 |
| __AkburaUseHookCompletionProbe | 0 | 0 | 0 | 0 | 0 | 0 |
| __AkburaSemanticProbe | 73 | 22 | 22 | 22 | 22 | 22 |

| Workload | Контексты | Запросы | Distinct / context | Cache16 factories |
|---|---:|---:|---|---:|
| ColdGeneration | 33 | 3188 | {1: 11, 2: 14, 3: 2, 4: 1, 5: 5} | 79 |
| NoChanges | 0 | 0 | {} | 0 |
| ComponentWhitespaceEdit | 0 | 0 | {} | 0 |
| CSharpCompilationEdit | 0 | 0 | {} | 0 |
| ComponentValueEdit | 9 | 787 | {1: 2, 2: 3, 3: 1, 5: 3} | 30 |
| ComponentBindingEdit | 1 | 34 | {3: 1} | 3 |
| ComponentContractEdit | 2 | 3 | {1: 1, 2: 1} | 3 |
| ReferencedCSharpRename | 34 | 3189 | {1: 12, 2: 14, 3: 2, 4: 1, 5: 5} | 80 |

NoChanges, ComponentWhitespaceEdit и добавление независимого C#-типа не обращаются к _componentTemplates: их время не является доводом в выборе кеша. ReferencedCSharpRename — отдельный содержательный сценарий переименования используемого символа. Contract edit использует существующую harness fixture; остальные component edits меняют настоящие файлы галереи.

Представительные cold-контексты (запросы / Cache16 misses; числа уникальных имён):

- `Components/CommandDemo.akbura`: 74 / 6; 5: __AkburaSemanticProbe=2/1, __AkburaUseHookProbe=10/2, __AkburaProbe=60/1, __AkburaStatementProbe=1/1, __AkburaComponentMethodProbe=1/1.
- `Components/ViewThisComponentInGithub.akbura`: 44 / 7; 5: __AkburaSemanticProbe=1/1, __AkburaProbe=4/1, __AkburaStatementProbe=2/2, __AkburaUseHookProbe=18/2, __AkburaComponentMethodProbe=19/1.
- `Components/ViewThisPageInGithub.akbura`: 37 / 7; 5: __AkburaSemanticProbe=1/1, __AkburaProbe=4/1, __AkburaStatementProbe=2/2, __AkburaUseHookProbe=15/2, __AkburaComponentMethodProbe=15/1.
- `Components/ViewThisInGithub.akbura`: 29 / 5; 5: __AkburaSemanticProbe=1/1, __AkburaProbe=2/1, __AkburaStatementProbe=1/1, __AkburaUseHookProbe=16/1, __AkburaComponentMethodProbe=9/1.
- `Components/Link.akbura`: 25 / 5; 5: __AkburaSemanticProbe=8/1, __AkburaProbe=10/1, __AkburaStatementProbe=1/1, __AkburaUseHookProbe=4/1, __AkburaComponentMethodProbe=2/1.

Внутреннее число запросов полного параллельного генератора может отличаться на один из-за конкурирующих semantic queries. Входы и последовательность generator operations одинаковы. Для точной атрибуции все процессы используют одинаковый frozen focused trace: 74 запроса CommandDemo (SHA256 focused-input.json сверяется).

| Вариант (cold) | Запросы | Hits | Factories | Повторные miss | Colliding keys | Serial replay evictions | Реально retained entries |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dictionary | 3189 | 3115 | 74 | 0 | 0 | 0 | 74 |
| Cache16 | 3188 | 3109 | 79 | 5 | 6 | 11 | 68 |
| Cache32 | 3190 | 3111 | 79 | 5 | 6 | 11 | 68 |
| Cache64 | 3190 | 3111 | 79 | 5 | 6 | 11 | 68 |
| Cache128 | 3188 | 3114 | 74 | 0 | 0 | 0 | 74 |

Retained entries прочитаны из реального хранилища после generation. Evictions — replay фактического порядка keys; некоторые контексты обслуживаются двумя потоками, поэтому это не линейризованный счётчик racing publications. Lookup hits/misses и factory counts собраны непосредственно перед фабрикой.

## Mixed probes: главный workload ObservedOrder

| Вариант | Mean ns/op | Median ns/op | Process mean min–max | B/op mean [min–max] | Hit ratio | Factories / pass min–max | Повторные min–max | Gen0 всего |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dictionary | 3202.4 | 3140.3 | 2995.6–3568.9 | 3000.23 [2998.41–3011.25] | 100.000% | 0–0 | 0–0 | 270 |
| Cache16 | 4024.7 | 3596.4 | 2990.4–5852.6 | 3103.88 [2998.76–3305.62] | 98.108% | 0–1024 | 0–1024 | 270 |
| Cache32 | 3669.4 | 3170.6 | 3118.6–5611.9 | 3059.42 [2998.41–3299.48] | 98.919% | 0–1024 | 0–1024 | 270 |
| Cache64 | 3364.6 | 3150.0 | 3030.6–4880.4 | 3015.05 [2998.41–3153.51] | 99.730% | 0–512 | 0–512 | 270 |
| Cache128 | 3300.8 | 3153.5 | 3053.2–4051.1 | 3015.01 [2998.41–3147.41] | 99.730% | 0–512 | 0–512 | 270 |

Mean — среднее process means; median — медиана process medians. Независимые наблюдения — процессы, а не три correlated samples одного процесса. Диапазон показывает variation между seeds и шумом ОС. Factories/rebuilds даны для одного measured pass, не суммированы по трём timed samples.

## Остальные focused workloads

| Распределение | Состояние | Dictionary ns/B | Cache16 ns/B | Cache32 ns/B | Cache64 ns/B | Cache128 ns/B |
|---|---|---:|---:|---:|---:|---:|
| Single | warm | 3039/2995.8 | 3107/2995.7 | 3061/2995.7 | 3064/2995.7 | 3023/2995.7 |
| Single | cold | 43750/10892.3 | 43545/10097.8 | 43882/10215.8 | 44034/10487.2 | 44119/10987.9 |
| TopFour | warm | 3070/2995.8 | 9242/4906.6 | 3137/4087.6 | 3104/3268.8 | 3176/3268.7 |
| TopFour | cold | 32241/9066.1 | 31837/8861.7 | 32262/8891.0 | 32275/8959.1 | 32330/9083.9 |
| ObservedNames | warm | 3112/2995.8 | 8188/4524.6 | 3161/3869.3 | 3112/3214.1 | 3124/3214.1 |
| ObservedNames | cold | 31157/8944.2 | 31016/8779.2 | 30619/8802.7 | 31195/8857.1 | 31383/8957.1 |
| ObservedOrder | warm | 3140/3000.2 | 3596/3103.9 | 3171/3059.4 | 3150/3015.0 | 3153/3015.0 |
| ObservedOrder | cold | 5663/3399.8 | 5727/3403.7 | 5673/3397.9 | 5691/3394.2 | 5634/3401.0 |
| VerifiedRoundRobin | warm | 3116/2995.8 | 15811/5676.3 | 11395/4733.4 | 8048/3740.7 | 7944/3591.7 |
| VerifiedRoundRobin | cold | 27359/8678.8 | 28801/8599.2 | 27549/8609.9 | 27523/8634.6 | 27684/8680.3 |
| Skewed | warm | 3055/2995.8 | 7202/3793.2 | 5876/3512.9 | 4731/3217.6 | 4586/3173.2 |
| Skewed | cold | 11347/4683.3 | 11718/4660.0 | 11791/4663.2 | 11614/4670.5 | 11468/4684.0 |
| ForcedCollision | warm | 3072/2995.8 | 26600/8455.3 | 26990/8455.2 | 26606/8455.3 | 26556/8455.3 |
| ForcedCollision | cold | 29733/8681.0 | 28453/8601.4 | 28287/8612.0 | 28703/8636.9 | 27826/8682.5 |

В таблице выше время — медиана process medians, B/op — среднее process means. Полные mean/median/ranges/bytes/hits/factories/Gen0: [Focused CSV](ComponentProbeCacheFocused.csv).

## Коллизии и стабильность между процессами

| Процесс | 16: пары observed | 32 | 64 | 128 |
|---|---|---|---|---|
| full-1 | 4:StatementProbe,UseHookProbe; 12:Probe,SemanticProbe | 20:StatementProbe,UseHookProbe; 12:Probe,SemanticProbe | 44:Probe,SemanticProbe | 108:Probe,SemanticProbe |
| focused-0 | — | — | — | — |
| focused-1 | — | — | — | — |
| focused-2 | 15:UseHookProbe,SemanticProbe | 15:UseHookProbe,SemanticProbe | — | — |
| focused-3 | — | — | — | — |
| focused-4 | 1:StatementProbe,SemanticProbe | — | — | — |
| focused-5 | — | — | — | — |
| focused-6 | — | — | — | — |
| focused-7 | 5:StatementProbe,UseHookProbe | — | — | — |
| cold-0 | 15:StatementProbe,Probe; 3:UseHookProbe,SemanticProbe | 3:UseHookProbe,SemanticProbe | — | — |

Полная таблица численных hashes/indexes всех 11 имён и всех процессов: [Buckets CSV](ComponentProbeCacheBuckets.csv). Индекс в точности hash & (size−1). В profiling seed full-0 StatementProbe и UseHookProbe конфликтуют в 16/32/64 слотах. В timing seed full-1 Probe и SemanticProbe конфликтуют даже в 128 слотах. В восьми первоначальных focused seeds коллизии реальных 5 имён были у Cache16 в 3 процессах, у Cache32 в одном, у 64/128 отсутствовали. Дополнительные seeds показывают, почему увеличенный кеш не гарантирует стабильности даже на пяти реальных ключах.

## FeatureGallery целиком

| Вариант | Cold median [min–max], ms | MB/run | Value edit, ms | Binding edit, ms | Contract edit, ms | C# add, ms | C# rename, ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dictionary | 13727.156 [13593.510–13759.235] | 3276.4594 | 8635.510 | 89.126 | 26.302 | 2.579 | 13480.663 |
| Cache16 | 13149.615 [13077.820–13198.585] | 3275.5170 | 9515.274 | 113.839 | 34.083 | 3.226 | 18503.856 |
| Cache32 | 16496.519 [16437.465–17530.864] | 3281.2868 | 10015.255 | 115.533 | 34.879 | 3.313 | 16503.446 |
| Cache64 | 17713.524 [16532.139–20236.571] | 3277.8998 | 9790.920 | 131.806 | 34.610 | 3.055 | 18647.198 |
| Cache128 | 13511.361 [13356.960–13598.012] | 3274.1266 | 8049.331 | 96.582 | 25.848 | 2.693 | 13929.909 |

Полные mean/median/min/max/MB/Gen0 всех восьми сценариев:

### CSharpCompilationEdit

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 2.556 | 2.579 | 2.370–2.719 | 0.5536 | 0 |
| Cache16 | 3.151 | 3.226 | 2.890–3.339 | 0.5824 | 0 |
| Cache32 | 3.324 | 3.313 | 2.841–3.818 | 0.6115 | 0 |
| Cache64 | 3.044 | 3.055 | 2.979–3.096 | 0.6401 | 0 |
| Cache128 | 2.843 | 2.693 | 2.413–3.422 | 0.5242 | 0 |

### ColdGeneration

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 13693.301 | 13727.156 | 13593.510–13759.235 | 3276.4594 | 1573 |
| Cache16 | 13142.007 | 13149.615 | 13077.820–13198.585 | 3275.5170 | 1568 |
| Cache32 | 16821.616 | 16496.519 | 16437.465–17530.864 | 3281.2868 | 1590 |
| Cache64 | 18160.745 | 17713.524 | 16532.139–20236.571 | 3277.8998 | 1578 |
| Cache128 | 13488.778 | 13511.361 | 13356.960–13598.012 | 3274.1266 | 1570 |

### ComponentBindingEdit

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 89.633 | 89.126 | 88.213–91.560 | 20.1215 | 9 |
| Cache16 | 117.613 | 113.839 | 107.248–131.754 | 20.1688 | 9 |
| Cache32 | 118.499 | 115.533 | 114.924–125.038 | 20.2081 | 9 |
| Cache64 | 134.463 | 131.806 | 129.389–142.195 | 20.2345 | 9 |
| Cache128 | 101.456 | 96.582 | 93.092–114.693 | 20.0976 | 9 |

### ComponentContractEdit

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 26.289 | 26.302 | 25.151–27.415 | 6.4440 | 3 |
| Cache16 | 33.371 | 34.083 | 31.902–34.128 | 6.4801 | 3 |
| Cache32 | 35.433 | 34.879 | 33.958–37.463 | 6.5073 | 3 |
| Cache64 | 36.344 | 34.610 | 33.999–40.423 | 6.5343 | 3 |
| Cache128 | 26.659 | 25.848 | 25.435–28.693 | 6.3864 | 3 |

### ComponentValueEdit

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 8638.130 | 8635.510 | 8514.867–8764.012 | 1479.0377 | 710 |
| Cache16 | 9409.235 | 9515.274 | 9023.330–9689.101 | 1479.3245 | 712 |
| Cache32 | 10080.879 | 10015.255 | 9871.989–10355.392 | 1479.2382 | 711 |
| Cache64 | 9610.567 | 9790.920 | 9156.224–9884.557 | 1479.7111 | 711 |
| Cache128 | 8038.933 | 8049.331 | 7972.225–8095.245 | 1479.1845 | 708 |

### ComponentWhitespaceEdit

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 14.706 | 8.406 | 1.970–33.743 | 0.9960 | 0 |
| Cache16 | 13.194 | 6.067 | 1.810–31.704 | 1.0249 | 0 |
| Cache32 | 15.878 | 7.885 | 2.194–37.557 | 1.0538 | 0 |
| Cache64 | 16.067 | 7.483 | 1.815–38.902 | 1.0623 | 0 |
| Cache128 | 14.309 | 6.199 | 1.910–34.819 | 0.9671 | 0 |

### NoChanges

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 0.264 | 0.265 | 0.237–0.289 | 0.0547 | 0 |
| Cache16 | 0.269 | 0.260 | 0.256–0.290 | 0.0588 | 0 |
| Cache32 | 0.356 | 0.353 | 0.311–0.405 | 0.0621 | 0 |
| Cache64 | 0.450 | 0.333 | 0.330–0.687 | 0.0808 | 0 |
| Cache128 | 0.363 | 0.337 | 0.337–0.415 | 0.0560 | 0 |

### ReferencedCSharpRename

| Вариант | Mean ms | Median ms | Min–max ms | Mean MB | Gen0 |
|---|---:|---:|---:|---:|---:|
| Dictionary | 13500.887 | 13480.663 | 13366.209–13655.790 | 3308.2398 | 1585 |
| Cache16 | 18360.124 | 18503.856 | 17891.679–18684.837 | 3311.7328 | 1599 |
| Cache32 | 16867.084 | 16503.446 | 16340.935–17756.872 | 3308.0209 | 1586 |
| Cache64 | 19454.293 | 18647.198 | 18229.348–21486.334 | 3311.3107 | 1598 |
| Cache128 | 14056.885 | 13929.909 | 13813.700–14427.045 | 3306.8824 | 1582 |

## Cold control: обратный порядок

Основная матрица: Cache128 → Dictionary → Cache16 → Cache32 → Cache64. Независимый cold control: Cache64 → Cache32 → Cache16 → Dictionary → Cache128. В каждой ячейке mean / median / min–max, ms; MB — среднее allocated MB/run.

| Вариант | Основной cold, ms | Обратный cold, ms | MB: основной / обратный |
|---|---:|---:|---:|
| Dictionary | 13693.3 / 13727.2 / 13593.5–13759.2 | 15216.4 / 15221.8 / 15184.7–15242.5 | 3276.459 / 3280.048 |
| Cache16 | 13142.0 / 13149.6 / 13077.8–13198.6 | 15347.4 / 15097.0 / 14991.1–15954.1 | 3275.517 / 3279.628 |
| Cache32 | 16821.6 / 16496.5 / 16437.5–17530.9 | 15222.6 / 15203.4 / 15175.9–15288.5 | 3281.287 / 3278.793 |
| Cache64 | 18160.7 / 17713.5 / 16532.1–20236.6 | 16037.4 / 15390.5 / 15327.4–17394.3 | 3277.900 / 3277.426 |
| Cache128 | 13488.8 / 13511.4 / 13357.0–13598.0 | 13922.0 / 13859.9 / 13799.6–14106.5 | 3274.127 / 3276.221 |

## Retained memory и parallel

| Вариант | Реальные 5 имён: B/cache median | Entries range | Все 11: B/cache median | Entries range | Parallel observed ns/op median | Parallel B/op mean |
|---|---:|---:|---:|---:|---:|---:|
| Dictionary | 6529.4 | 5–5 | 13318.2 | 11–11 | 4356.1 | 7853.1 |
| Cache16 | 5245.2 | 3–5 | 9186.2 | 6–9 | 4407.8 | 8077.5 |
| Cache32 | 5934.8 | 3–5 | 10434.1 | 8–10 | 4409.2 | 7966.5 |
| Cache64 | 6185.1 | 4–5 | 11819.0 | 9–11 | 4269.3 | 7890.5 |
| Cache128 | 6703.8 | 4–5 | 12338.0 | 10–11 | 4390.1 | 8001.0 |

Retained memory — after-GC delta для 128 одновременно удерживаемых populated caches при живом real model; включает cache/entry/syntax objects, не всю Roslyn compilation. Это управляемая оценка, не precise heap graph или peak RSS; sharing/JIT liveness влияют на числа. Параллельный проход — 8192 запросов; обычный и constant-hash stress, сравнение полного результата при каждом запросе. Actual factory/hit counts параллельного profiling — в *-profile.json.

## Ограничения и выводы

В основном ObservedOrder повторные фабрики есть у Cache16 в 5/10 процессах, Cache32 — 3/10, Cache64 — 1/10, Cache128 — 1/10. Dictionary — 0/10. Для bounded кешей это до 1024 лишних фабрик на 18944 запроса, то есть повторное разрешение component type и создание syntax, а не только лишний lookup. До 5,85 мкс/запрос против примерно 3,2 мкс среднего Dictionary. В одном seed даже Cache128 имеет 512 повторных фабрик, около 4,05 мкс/запрос, Dictionary в том же процессе — около 3,00 мкс. Все такие запросы дают идентичный итоговый синтаксис.

Галерея использует ровно пять имён из полного набора 11. Round-robin всех 11 и constant-hash stress приведены отдельно и не заменяют реальный trace. Даже без этих stress случаев у всех четырёх bounded размеров уже обнаружена проблема на ObservedOrder. При 11 именах столкновения ещё чаще, но выбор Dictionary не основан только на этом распределении.

Dictionary удерживает все пять шаблонов (около 6,5 КиБ/cache) и все 11 (около 13 КиБ/cache). Медиана Cache64 на пяти реальных именах — около 6,2 КиБ, экономия порядка 0,34 КиБ; Cache128 в целом дороже Dictionary при пяти полностью сохранённых шаблонах. Меньшая retained memory Cache16 частично объясняется тем, что реально остаётся только 3–5 entries. Эта экономия требует повторной работы. Все кеши принадлежат модели и освобождаются вместе с ней. Bounded entries не ограничивают общий объём Roslyn syntax: результаты запросов могут продолжать удерживать syntax, разделённый с вытесненным шаблоном.

В whole-generator запуске около 3,27–3,28 ГБ transient allocations и 13–20 секунд на cold generation. Доля template cache мала. Порядок вариантов/длительный прогон дают большой разброс: например, Cache32 и Cache64 существенно меняют время в обратном control. Даже NoChanges/C# add, где нет обращений к этому кешу, меняются между вариантами. Поэтому нельзя приписать эти различия выбранной структуре или заявить подтверждённое ускорение cold generation. Количество allocations всего генератора тоже колеблется на мегабайты, намного больше разницы небольших cache objects.

Для focused cold numbers учитывайте reflection/Activator overhead, различающийся у конструкторов Dictionary и ConcurrentCache; они не использованы как доказательство выигрыша конструктора. Основной тёплый таймер вызывает только compiled production method, result construction и checksum. Parallel results включают проверку ToFullString, поэтому это более широкий workload; tracing parallel factories меняет scheduling. Performance thresholds не добавлены.

Аппаратная среда: Intel Core i5-11400H, 6 cores / 12 logical processors, около 16 ГБ RAM, Windows, .NET 10.0.12. Из-за дрейфа времени E2E вывод намеренно ограничен; увеличение числа samples не превращает эти данные в доказательство небольшого whole-compiler выигрыша.

## Проверка

45 проверок вариант/сценарий: 8 сценариев × 5 вариантов в полной матрице и 5 cold controls. Каждый проверен против fresh output; все кандидаты дополнительно сопоставлены напрямую с исходным Cache16. Холодная галерея содержит 39 generated sources, 547 Roslyn compiler diagnostics, 0 generator/driver diagnostics; ошибок нет. Сохраняются Akbura diagnostic order, ID/message/severity/properties, original/mapped paths и spans; Roslyn diagnostics канонизированы по порядку. Нормализован только временный virtual project root.
10 процессов × 5 вариантов: одинаковый focused output fingerprint; parallel outputs проверены; cancellation проверен для каждого варианта. Все sequential simulations в дополнительных процессах совпали с фактическими инструментированными factory/rebuild счётчиками.

Сборки Generator, BlackSilence и benchmark project пройдены в **Release, Debug, ReleaseStats, DebugStats**. Основные performance данные — только Release без STATS.

| Набор | Passed | Failed / skipped |
|---|---:|---:|
| Release: probe/binding, symbols, semantic state, generator/incremental/FeatureGallery regressions | 188 | 0 / 0 |
| Release: финальные generic/cache regressions и оставшиеся observational allocation tests | 34 | 0 / 0 |
| Release: diagnostics snapshots/failures/transport/retry | 36 | 0 / 0 |
| Debug: cache, probes и semantic architecture | 135 | 0 / 0 |
| ReleaseStats: тот же focused correctness набор + STATS cases | 138 | 0 / 0 |
| DebugStats: тот же набор | 138 | 0 / 0 |

Release наборы частично пересекаются: это не сумма уникальных тестов. Final 34 включает новую проверку одного общего ключа под параллельными запросами. Сохранены generic ConcurrentCache eviction/concurrency tests и добавлен rebuild настоящего template после принудительного вытеснения. Production dictionary тестируется с принудительными hash collisions, canonical publication и независимыми members/usings. Старый reflection-based `MeasureTemplateCacheSizes` не содержал assertions и заменён benchmark с независимыми compiled variants; проверок правильности не удалено.

Порядок Akbura/driver diagnostics сохранён. Roslyn compiler diagnostics сравниваются в каноническом порядке после отдельно доказанной нестабильности их перечисления; тексты/ID/severity/properties/paths/spans не исключаются. Есть непустые diagnostics в дополнительных 36 регрессиях. Cancellation и parallel syntax equality проверены для каждого варианта.

Существующие предупреждения: CS8602 в Blender.Reader, пять xUnit warnings в других тестах и шесть NuGet audit warnings benchmark dependencies. Новых предупреждений/подавлений нет. `git diff --check` выполнен; все изменённые текстовые файлы UTF-8, CRLF, завершающий CRLF. Furioso, PooledDelegates, resolver fast hit path и ConcurrentCache internals не изменены.

## Воспроизведение

Из корня репозитория, после обычного restore и наличия reference build outputs:

```powershell
.\src\Akbura.Benchmarks\Compare-ComponentProbeCache.ps1 -Output D:\CodexBenchmarks\probe-cache-reproduction -Processes 1 -ColdProcesses 1 -FocusedProcesses 8 -Warmups 1 -Iterations 3
```

Скрипт строит 5 обычных и 5 инструментированных DLL вне tracked tree, временно меняет только CSharpProbeContext и восстанавливает его исходные bytes в finally. Production Release instrumentation не содержит. Не запускайте параллельно другие сборки/бенчмарки. BuildOnly подготавливает DLL без запусков. Отдельный focused run требует focused-input.json из полного profile в output directory. Raw outputs этого опыта: `D:/CodexBenchmarks/Akbura-component-probe-cache-20261024`. Скрипт проверяет минимум 1 ГБ свободного места до начала. Его default Output — `%TEMP%/Akbura-component-probe-cache`; на заполненном C: укажите другой локальный диск. Старые прерванные матрицы сохранены отдельно и не используются в финальных timing tables.

## Коммит

Один commit: `perf(generator): tune component probe template caching`, parent `13fc40e32e9de93607314044993e5b8a6aec6d86`. SHA этого commit можно получить командой `git log -1 --format=%H -- src/Akbura.Benchmarks/ComponentProbeCacheResults.md`.

Изменённые файлы:

- `src/Akbura.Generator/Language/Binder/CSharpProbeContext.cs`
- `src/Akbura.UnitTests/ConcurrentCacheRegressionTests.cs`
- `src/Akbura.UnitTests/ConcurrentCacheAllocationTests.cs`
- `src/Akbura.Benchmarks/Program.cs`
- `src/Akbura.Benchmarks/FeatureGalleryBenchmarkProject.cs`
- `src/Akbura.Benchmarks/FeatureGalleryComponentProbeComparison.cs`
- `src/Akbura.Benchmarks/Compare-ComponentProbeCache.ps1`
- `src/Akbura.Benchmarks/ComponentProbeCacheResults.md`
- `src/Akbura.Benchmarks/ComponentProbeCacheFocused.csv`
- `src/Akbura.Benchmarks/ComponentProbeCacheBuckets.csv`
