-- ============================================================================
--  Development seed data
--
--  Runs after 01-create-db.sql: the Aspire AppHost mounts configs/postgres/init/ into
--  /docker-entrypoint-initdb.d, and PostgreSQL executes the files in name order, once, against the
--  fresh database. The local database has no volume, so every AppHost run starts with exactly this data.
--
--  Local development only. The test harness loads 01-create-db.sql alone, so tests start empty, and
--  production never runs these scripts.
--
--  Staff live in Entra, not in the database (ADR-D004). The seed rows point at placeholder object ids
--  that belong to no Entra user, so a staff screen resolving them finds nobody:
--    00000000-0000-0000-0000-00000000aaaa  the hiring manager who "created" every post and owns it (created_by,
--                                          hiring_manager_id)
--    00000000-0000-0000-0000-00000000bbbb  the recruiter responsible for them (recruiter_id)
-- ============================================================================

-- ---------------------------------------------------------------------------
-- recruitment.job_post
-- 16 posts: 13 published, enough for two pages of the public list (10 a page), 1 closed and 2 drafts that only
-- staff see. Between them they use every employment type and payment type. created_at is spread over the past weeks,
-- so the newest-first order looks like a real board rather than one insert.
-- ---------------------------------------------------------------------------
INSERT INTO recruitment.job_post
    (title, location, department, description, status, open_positions, salary, payment_type,
     employment_type, recruiter_id, hiring_manager_id, created_by, created_at)
VALUES
    ('Elektrotechnik údržby', 'Bratislava', 'Údržba',
     'Staráte sa o bezporuchový chod elektrických zariadení vo výrobnej hale: preventívne prehliadky, diagnostika porúch a opravy.

Ponúkame prácu na zmeny, príplatky za nočné a víkendové zmeny a hradené školenia.

Požadujeme vyhlášku 508/2009 Z. z. (§ 21 alebo § 22) a aspoň dva roky praxe.',
     'published', 2, 1650.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '1 day'),

    ('Operátor CNC strojov', 'Trnava', 'Výroba',
     'Obsluhujete CNC sústruhy a frézy, kontrolujete rozmery dielcov podľa výkresovej dokumentácie a zapisujete výsledky merania.

Zaučíme vás; výhodou je skúsenosť so systémami Sinumerik alebo Heidenhain.',
     'published', 4, 9.80, 'hourly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '2 days'),

    ('Vývojár softvéru (.NET)', 'Žilina', 'Technológie',
     'Vyvíjate interné aplikácie na plánovanie výroby v C# a ASP.NET Core, navrhujete REST API a píšete testy.

Pracujeme v malom tíme so zavedeným code review a CI. Dva dni v týždni môžete pracovať z domu.',
     'published', 1, 2800.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '3 days'),

    ('Skladník', 'Senec', 'Logistika',
     'Príjem a výdaj tovaru, vychystávanie objednávok so skenerom a práca s vysokozdvižným vozíkom.

Preukaz na VZV je výhodou, nie podmienkou: kurz vám zaplatíme.',
     'published', 3, 8.50, 'hourly', 'fixed_term',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '4 days'),

    ('Brigáda: pomocník vo výrobe', 'Nitra', 'Výroba',
     'Jednoduchá montáž a balenie dielcov na linke. Vhodné pre študentov: zmeny si vyberáte podľa rozvrhu.

Podmienkou je status študenta a vek nad 18 rokov.',
     'published', 6, 7.20, 'hourly', 'student_work_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '5 days'),

    ('Vedúci výroby', 'Košice', 'Výroba',
     'Riadite tri výrobné linky a tím 40 ľudí, zodpovedáte za plnenie plánu, kvalitu a bezpečnosť práce.

Očakávame skúsenosť s riadením výroby v automotive alebo strojárstve a znalosť princípov lean manufacturingu.',
     'published', 1, 42000.00, 'yearly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '6 days'),

    ('Špecialista náboru', 'Bratislava', 'Ľudské zdroje',
     'Vediete náborový proces od inzerátu po nástup: oslovujete kandidátov, vediete pohovory a spolupracujete s manažérmi.

Hľadáme komunikatívneho človeka, ktorý rád pracuje s ľuďmi.',
     'published', 1, 1900.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '8 days'),

    ('Účtovník / účtovníčka', 'Banská Bystrica', 'Financie',
     'Spracúvate prijaté a vydané faktúry, bankové výpisy a podklady pre mesačnú uzávierku.

Pracovný čas 6 hodín denne, vhodné aj pre rodičov po materskej dovolenke.',
     'published', 1, 1250.00, 'monthly', 'part_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '10 days'),

    ('Kontrolór kvality', 'Trnava', 'Kvalita',
     'Kontrolujete dielce na vstupe aj výstupe z výroby, meriate na súradnicovom meracom stroji a vediete záznamy o nezhodách.

Ide o zástup počas rodičovskej dovolenky na 12 mesiacov s možnosťou predĺženia.',
     'published', 1, 1450.00, 'monthly', 'fixed_term',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '12 days'),

    ('Technický prekladateľ (nemčina)', 'Bratislava', 'Technológie',
     'Prekladáte technickú dokumentáciu a návody na obsluhu strojov z nemčiny do slovenčiny, približne 40 strán mesačne.

Prácu robíte z domu, termíny si dohodneme vopred.',
     'published', 1, 14.00, 'hourly', 'work_activity_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '14 days'),

    ('Inventúra skladu (jednorazovo)', 'Senec', 'Logistika',
     'Pomoc pri koncoročnej inventúre skladu: počítanie zásob a zápis do skenera. Práca na dva víkendy v decembri.',
     'published', 8, 9.00, 'hourly', 'work_performance_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '16 days'),

    ('Elektroprojektant', 'Žilina', 'Projekcia',
     'Navrhujete elektroinštalácie a rozvádzače pre nové výrobné linky v EPLAN-e, od projektu po uvedenie do prevádzky.

Spolupráca je na živnosť s dlhodobým výhľadom, rozsah približne 30 hodín týždenne.',
     'published', 1, 28.00, 'hourly', 'self_employed',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '18 days'),

    ('Mechanik strojov', 'Prešov', 'Údržba',
     'Opravujete a nastavujete mechanické časti výrobných strojov: prevodovky, hydrauliku a pneumatiku.

Ponúkame stabilnú prácu, 13. a 14. plat a príspevok na dopravu.',
     'published', 2, 1500.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '21 days'),

    -- Closed (uzavretá): filled, so hidden from applicants until a hiring manager reopens it.
    ('Asistent/ka nákupu', 'Košice', 'Nákup',
     'Pripravujete objednávky u dodávateľov, sledujete termíny dodania a aktualizujete údaje v ERP systéme.

Výhodou je angličtina na komunikatívnej úrovni.',
     'closed', 1, 1300.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '25 days'),

    -- Drafts (koncepty): never in the public list or detail.
    ('Vedúci logistiky', 'Senec', 'Logistika',
     'Koncept: náplň práce ešte dopĺňame s vedením skladu.',
     'draft', 1, 36000.00, 'yearly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '2 hours'),

    ('Brigáda: letná výpomoc v sklade', 'Nitra', 'Logistika',
     'Koncept: zverejníme v máji.',
     'draft', 5, 7.00, 'hourly', 'student_work_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', '00000000-0000-0000-0000-00000000aaaa', now() - interval '1 hour');

-- ---------------------------------------------------------------------------
-- recruitment.application
-- Six people applied to 'Elektrotechnik údržby', one in each application status, so the staff job post detail shows the
-- whole pipeline; two applied to 'Vývojár softvéru (.NET)', one of them (Martin Kašuba) the same person as above, which
-- is simply another row (ADR-D010). Names carry accents so the accent-insensitive search has something to find
-- ("kasuba", "stefanik"). No application_document rows: their files would have to exist in files.file and in the local
-- Azurite container, and staff only see scanned files anyway.
-- ---------------------------------------------------------------------------
INSERT INTO recruitment.application (job_post_id, first_name, last_name, email, phone_number, status, created_at)
SELECT p.id, a.first_name, a.last_name, a.email, a.phone_number, a.status, a.created_at
FROM (VALUES
        ('Elektrotechnik údržby',   'Martin',   'Kašuba',      'kasuba7@stud.uniza.sk',        '+421905123456', 'nová žiadosť', now() - interval '20 hours'),
        ('Elektrotechnik údržby',   'Zuzana',   'Štefániková', 'zuzana.stefanikova@gmail.com', '+421911222333', 'predvýber',    now() - interval '19 hours'),
        ('Elektrotechnik údržby',   'Peter',    'Hudák',       'peter.hudak@azet.sk',          '+421902444555', 'pohovor',      now() - interval '18 hours'),
        ('Elektrotechnik údržby',   'Lucia',    'Ďurišová',    'lucia.durisova@centrum.sk',    '+421948666777', 'ponuka',       now() - interval '17 hours'),
        ('Elektrotechnik údržby',   'Tomáš',    'Grešák',      'tomas.gresak@post.sk',         '+421907888999', 'prijatý',      now() - interval '16 hours'),
        ('Elektrotechnik údržby',   'Katarína', 'Poláková',    'k.polakova@gmail.com',         NULL,            'odmietnutý',   now() - interval '15 hours'),
        ('Vývojár softvéru (.NET)', 'Martin',   'Kašuba',      'kasuba7@stud.uniza.sk',        '+421905123456', 'predvýber',    now() - interval '13 hours'),
        ('Vývojár softvéru (.NET)', 'Ján',      'Mikloš',      'jan.miklos@outlook.com',       '+420602303404', 'nová žiadosť', now() - interval '14 hours')
     ) AS a (title, first_name, last_name, email, phone_number, status, created_at)
JOIN recruitment.job_post p ON p.title = a.title;
