using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;

namespace ShiftFlow.Infrastructure.Data;

public static class DbSeeder
{
    private static readonly (string Name, string NameAr)[] AllRoles =
    [
        ("Admin", "مسؤول النظام"), ("OperationsManager", "مدير العمليات"), ("Engineer", "مهندس"), ("HR", "الموارد البشرية"),
        ("Supervisor", "مشرف"), ("Section Head", "رئيس قسم"), ("Senior Engineer", "مهندس أول"),
        ("Operation Engineer", "مهندس تشغيل"), ("Technician", "فني"),
        ("Vendor", "المورد"),
    ];

    public static async Task SeedAsync(ApplicationDbContext db, UserManager<ApplicationUser> um, RoleManager<ApplicationRole> rm)
    {
        await db.Database.MigrateAsync();

        foreach (var role in AllRoles)
        {
            if (!await rm.RoleExistsAsync(role.Name))
                await rm.CreateAsync(new ApplicationRole(role.Name) { NameAr = role.NameAr });
            else
            {
                var existing = await rm.FindByNameAsync(role.Name);
                if (existing is not null && existing.NameAr is null)
                {
                    existing.NameAr = role.NameAr;
                    await rm.UpdateAsync(existing);
                }
            }
        }

        // Idempotent "add what's missing by Code" — same pattern as SeedAssetManagementAsync's
        // Areas fill-in, so re-running on a DB that already has some locations still tops up the rest.
        var seedLocations = new[]
        {
            new Location { Name = "Main Station - Kuwait City",   NameAr = "المحطة الرئيسية",     Code = "KW-001", City = "Kuwait City",        Status = "Active", Latitude = 29.3759, Longitude = 47.9774 },
            new Location { Name = "South Station - Ahmadi",       NameAr = "محطة الأحمدي",         Code = "KW-002", City = "Ahmadi",             Status = "Active", Latitude = 29.0769, Longitude = 48.0842 },
            new Location { Name = "North Station - Jahra",        NameAr = "محطة الجهراء",         Code = "KW-003", City = "Jahra",              Status = "Active", Latitude = 29.3375, Longitude = 47.6581 },
            new Location { Name = "West Station - Farwaniya",     NameAr = "محطة الفروانية",       Code = "KW-004", City = "Farwaniya",          Status = "Active", Latitude = 29.2977, Longitude = 47.9391 },
            new Location { Name = "East Station - Mubarak Al-Kabeer", NameAr = "محطة مبارك الكبير", Code = "KW-005", City = "Mubarak Al-Kabeer",  Status = "Active", Latitude = 29.1961, Longitude = 48.0704 },
            new Location { Name = "Coastal Station - Hawalli",    NameAr = "محطة حولي الساحلية",   Code = "KW-006", City = "Hawalli",            Status = "Active", Latitude = 29.3328, Longitude = 48.0263 },
        };
        var existingLocationCodes = (await db.Locations.Select(l => l.Code).ToListAsync()).ToHashSet();
        var missingLocations = seedLocations.Where(l => !existingLocationCodes.Contains(l.Code)).ToList();
        if (missingLocations.Count > 0)
        {
            db.Locations.AddRange(missingLocations);
            await db.SaveChangesAsync();
        }

        async Task CreateUser(string email, string pass, string name, string role, string emp)
        {
            if (await um.FindByEmailAsync(email) != null) return;
            var u = new ApplicationUser
            {
                UserName = email, Email = email, FullName = name,
                EmployeeNumber = emp, Department = role, IsActive = true, EmailConfirmed = true
            };
            var r = await um.CreateAsync(u, pass);
            if (r.Succeeded) await um.AddToRoleAsync(u, role);
        }

        await CreateUser("admin@shiftflow.com",    "Admin@123456",   "System Administrator", "Admin",            "EMP-0001");
        await CreateUser("manager@shiftflow.com",  "Manager@123456", "Ahmed Al-Rashidi",     "OperationsManager", "EMP-0002");
        await CreateUser("engineer@shiftflow.com", "Engineer@123456","Khalid Al-Mutairi",    "Engineer",         "EMP-0003");
        await CreateUser("hr@shiftflow.com",       "Hr@123456",      "Sara Al-Kandari",      "HR",               "EMP-0004");

        await SeedPermissionsAsync(db, rm);
        await SeedAssetManagementAsync(db, um);
    }

    /// <summary>Fixed, not user-editable — see Governorate.cs.</summary>
    private static readonly (string Name, string NameAr, double Lat, double Lng)[] KuwaitGovernorates =
    [
        ("Al Asimah", "العاصمة", 29.3759, 47.9774),
        ("Hawalli", "حولي", 29.3328, 48.0263),
        ("Farwaniya", "الفروانية", 29.2977, 47.9391),
        ("Mubarak Al-Kabeer", "مبارك الكبير", 29.1961, 48.0704),
        ("Ahmadi", "الأحمدي", 29.0769, 48.0838),
        ("Jahra", "الجهراء", 29.3375, 47.6581),
    ];

    /// <summary>Fixed, not user-editable — same as Governorates. Every real area under each governorate is listed here up front, not created ad hoc.</summary>
    private static readonly (string Governorate, string Name, string NameAr)[] KuwaitAreas =
    [
        ("Al Asimah", "Kuwait City", "مدينة الكويت"), ("Al Asimah", "Sharq", "شرق"), ("Al Asimah", "Mirqab", "المرقاب"),
        ("Al Asimah", "Qibla", "القبلة"), ("Al Asimah", "Dasman", "دسمان"), ("Al Asimah", "Daiya", "الدعية"),
        ("Al Asimah", "Qadsiya", "القادسية"), ("Al Asimah", "Shamiya", "الشامية"), ("Al Asimah", "Faiha", "الفيحاء"),
        ("Al Asimah", "Nuzha", "النزهة"), ("Al Asimah", "Khaldiya", "الخالدية"), ("Al Asimah", "Adailiya", "العديلية"),
        ("Al Asimah", "Rawda", "الروضة"), ("Al Asimah", "Surra", "السرة"), ("Al Asimah", "Yarmouk", "اليرموك"),
        ("Al Asimah", "Qurtuba", "قرطبة"), ("Al Asimah", "Shuwaikh", "الشويخ"), ("Al Asimah", "Kaifan", "كيفان"),
        ("Al Asimah", "Doha", "الدوحة"), ("Al Asimah", "Bnaid Al-Qar", "بنيد القار"),

        ("Hawalli", "Hawalli", "حولي"), ("Hawalli", "Salmiya", "السالمية"), ("Hawalli", "Jabriya", "الجابرية"),
        ("Hawalli", "Bayan", "بيان"), ("Hawalli", "Mishref", "مشرف"), ("Hawalli", "Salwa", "سلوى"),
        ("Hawalli", "Rumaithiya", "الرميثية"), ("Hawalli", "Shaab", "الشعب"), ("Hawalli", "Shuhada", "الشهداء"),
        ("Hawalli", "Zahra", "الزهراء"), ("Hawalli", "Bidaa", "بيدع"), ("Hawalli", "Maidan Hawalli", "ميدان حولي"),

        ("Farwaniya", "Farwaniya", "الفروانية"), ("Farwaniya", "Khaitan", "خيطان"), ("Farwaniya", "Jleeb Al-Shuyoukh", "جليب الشيوخ"),
        ("Farwaniya", "Andalous", "الأندلس"), ("Farwaniya", "Ardiya", "العارضية"), ("Farwaniya", "Rabiya", "الرابية"),
        ("Farwaniya", "Rai", "الري"), ("Farwaniya", "Omariya", "العمرية"), ("Farwaniya", "Ferdous", "الفردوس"),
        ("Farwaniya", "Abdullah Al-Mubarak", "عبدالله المبارك"), ("Farwaniya", "Sabah Al-Nasser", "صباح الناصر"), ("Farwaniya", "Reggai", "الرقعي"),

        ("Mubarak Al-Kabeer", "Mubarak Al-Kabeer", "مبارك الكبير"), ("Mubarak Al-Kabeer", "Adan", "العدان"),
        ("Mubarak Al-Kabeer", "Qusour", "القصور"), ("Mubarak Al-Kabeer", "Qurain", "القرين"),
        ("Mubarak Al-Kabeer", "Sabah Al-Salem", "صباح السالم"), ("Mubarak Al-Kabeer", "Messila", "المسيلة"),
        ("Mubarak Al-Kabeer", "Abu Ftaira", "أبو فطيرة"), ("Mubarak Al-Kabeer", "Fnaitees", "الفنيطيس"),

        ("Ahmadi", "Ahmadi", "الأحمدي"), ("Ahmadi", "Fahaheel", "الفحيحيل"), ("Ahmadi", "Mangaf", "المنقف"),
        ("Ahmadi", "Abu Halifa", "أبو حليفة"), ("Ahmadi", "Fintas", "الفنطاس"), ("Ahmadi", "Mahboula", "المهبولة"),
        ("Ahmadi", "Riqqa", "الرقة"), ("Ahmadi", "Sabahiya", "الصباحية"), ("Ahmadi", "Wafra", "الوفرة"),
        ("Ahmadi", "Jaber Al-Ali", "جابر العلي"), ("Ahmadi", "Hadiya", "هدية"), ("Ahmadi", "Zour", "الزور"),

        ("Jahra", "Jahra", "الجهراء"), ("Jahra", "Naeem", "النعيم"), ("Jahra", "Qasr", "القصر"),
        ("Jahra", "Waha", "الواحة"), ("Jahra", "Sulaibiya", "الصليبية"), ("Jahra", "Taima", "تيماء"),
        ("Jahra", "Oyoun", "العيون"), ("Jahra", "Abdali", "العبدلي"), ("Jahra", "Nasseem", "النسيم"),
        ("Jahra", "Saad Al-Abdullah", "سعد العبدالله"),
    ];

    private static async Task SeedAssetManagementAsync(ApplicationDbContext db, UserManager<ApplicationUser> um)
    {
        if (!await db.Governorates.AnyAsync())
        {
            db.Governorates.AddRange(KuwaitGovernorates.Select(g =>
                new Governorate { Name = g.Name, NameAr = g.NameAr, CenterLat = g.Lat, CenterLng = g.Lng }));
            await db.SaveChangesAsync();
        }

        // Fixed reference data — same idempotent "add what's missing" pattern as SeedPermissionsAsync,
        // so re-running this on an existing DB fills in any areas that weren't there yet without
        // touching ones that already exist (e.g. ones with Zones already attached to them).
        var governoratesByName = await db.Governorates.ToDictionaryAsync(g => g.Name);
        var existingAreas = await db.Areas.Select(a => new { a.GovernorateId, a.Name }).ToListAsync();
        var existingAreaKeys = existingAreas.Select(a => (a.GovernorateId, a.Name)).ToHashSet();
        var missingAreas = KuwaitAreas
            .Where(a => governoratesByName.ContainsKey(a.Governorate) && !existingAreaKeys.Contains((governoratesByName[a.Governorate].Id, a.Name)))
            .Select(a => new Area { Name = a.Name, NameAr = a.NameAr, GovernorateId = governoratesByName[a.Governorate].Id })
            .ToList();
        if (missingAreas.Count > 0)
        {
            db.Areas.AddRange(missingAreas);
            await db.SaveChangesAsync();
        }

        // Blocks are fixed reference data too — like Area, not admin-editable. Real block counts,
        // each individually confirmed against that area's own Wikipedia article (a quoted sentence
        // stating its block count) — NOT the "Areas of Kuwait" aggregated table, which turned out to
        // misreport several values (e.g. it claimed Jabriya=14/Bayan=14/Doha=5/Hawalli=11 — the real,
        // per-article figures are 12/13/7/12) and is not used as a source here. "Kuwait City" itself
        // is the historic old-town core and genuinely has 0 numbered blocks per Wikipedia — it gets a
        // single nominal Block 1 purely as an anchor since every Zone needs a Block to attach to.
        // Areas with no individually-confirmed block count fall back to 1 (an honest "unconfirmed"
        // placeholder, not a fabricated number) via GetValueOrDefault below.
        var blockCountByArea = new Dictionary<string, int>
        {
            // Al Asimah (Capital)
            ["Kuwait City"] = 1, ["Shamiya"] = 10, ["Faiha"] = 9, ["Khaldiya"] = 4, ["Adailiya"] = 4,
            ["Rawda"] = 5, ["Surra"] = 6, ["Yarmouk"] = 4, ["Shuwaikh"] = 8, ["Doha"] = 7,
            // Hawalli
            ["Hawalli"] = 12, ["Salmiya"] = 12, ["Jabriya"] = 12, ["Bayan"] = 13, ["Rumaithiya"] = 12, ["Zahra"] = 8,
            // Farwaniya
            ["Khaitan"] = 10, ["Jleeb Al-Shuyoukh"] = 5,
            // Mubarak Al-Kabeer
            ["Sabah Al-Salem"] = 13,
            // Ahmadi
            ["Fahaheel"] = 14, ["Mangaf"] = 10,
            // Jahra
            ["Jahra"] = 12, ["Sulaibiya"] = 10, ["Saad Al-Abdullah"] = 10,
        };
        var allAreas = await db.Areas.ToListAsync();
        var existingBlockKeys = (await db.Blocks.Select(b => new { b.AreaId, b.Number }).ToListAsync())
            .Select(b => (b.AreaId, b.Number)).ToHashSet();
        var missingBlocks = allAreas
            .SelectMany(a => Enumerable.Range(1, blockCountByArea.GetValueOrDefault(a.Name, 1))
                .Where(n => !existingBlockKeys.Contains((a.Id, n)))
                .Select(n => new Block { AreaId = a.Id, Number = n }))
            .ToList();
        if (missingBlocks.Count > 0)
        {
            db.Blocks.AddRange(missingBlocks);
            await db.SaveChangesAsync();
        }

        // Idempotent "add what's missing by Name" — same fill-in pattern as Areas/Blocks above,
        // so re-running on a partially-seeded DB tops up the rest without touching existing zones.
        var areasByName = await db.Areas.ToDictionaryAsync(a => a.Name);
        var blocksByAreaAndNumber = (await db.Blocks.ToListAsync()).ToDictionary(b => (b.AreaId, b.Number));
        var seedZones = new (string Area, int Block, string Name, string NameAr, string Address, double? Lat, double? Lng)[]
        {
            ("Jabriya",         1, "Block 3 Substation",          "محطة القطعة 3",              "Building 7, Street 1, Block 3, Jabriya",           29.3210, 48.0175),
            ("Kuwait City",     1, "Central Admin Building",      "مبنى الإدارة المركزية",       "Admin Tower, Street 20, Block 1, Kuwait City",     29.3759, 47.9774),
            ("Kuwait City",     1, "North Control Center",        "مركز التحكم الشمالي",          "Control Building, Street 5, Kuwait City",  null,    null), // no coordinates — demonstrates the optional case; Kuwait City has no real numbered blocks, so both its zones share the nominal Block 1
            ("Salmiya",         1, "Salmiya Substation",          "محطة السالمية",              "Building 12, Street 4, Block 1, Salmiya",          29.3394, 48.0758),
            ("Fahaheel",        1, "Fahaheel Pumping Station",    "محطة ضخ الفحيحيل",            "Pump House, Street 9, Block 1, Fahaheel",          29.0847, 48.1319),
            ("Jahra",           1, "Jahra Water Treatment Plant", "محطة معالجة مياه الجهراء",     "Treatment Plant, Street 3, Block 1, Jahra",        29.3375, 47.6581),
            ("Khaitan",         1, "Khaitan Distribution Center", "مركز توزيع خيطان",             "Distribution Bldg, Street 15, Block 1, Khaitan",   29.2775, 47.9569),
            ("Sabah Al-Salem",  1, "Sabah Al-Salem Substation",   "محطة صباح السالم",             "Substation, Street 6, Block 1, Sabah Al-Salem",    29.1500, 48.1167),
            ("Shuwaikh",        1, "Shuwaikh Warehouse",          "مستودع الشويخ",                "Warehouse 2, Street 11, Block 1, Shuwaikh",        29.3400, 47.9350),
            ("Mangaf",          1, "Mangaf Control Room",         "غرفة تحكم المنقف",             "Control Room, Street 8, Block 1, Mangaf",          29.0833, 48.1333),
            ("Rumaithiya",      1, "Rumaithiya Substation",       "محطة الرميثية",                "Building 4, Street 2, Block 1, Rumaithiya",        29.3167, 48.0500),
            ("Sulaibiya",       1, "Sulaibiya Pumping Station",   "محطة ضخ الصليبية",             "Pump House, Street 7, Block 1, Sulaibiya",         29.3833, 47.6167),
        };
        var existingZoneNames = (await db.Zones.Select(z => z.Name).ToListAsync()).ToHashSet();
        var missingZones = seedZones
            .Where(z => areasByName.ContainsKey(z.Area) && blocksByAreaAndNumber.ContainsKey((areasByName[z.Area].Id, z.Block)) && !existingZoneNames.Contains(z.Name))
            .Select(z => new Zone
            {
                Name = z.Name, NameAr = z.NameAr, BlockId = blocksByAreaAndNumber[(areasByName[z.Area].Id, z.Block)].Id,
                Address = z.Address, Latitude = z.Lat, Longitude = z.Lng,
            })
            .ToList();
        if (missingZones.Count > 0)
        {
            db.Zones.AddRange(missingZones);
            await db.SaveChangesAsync();
        }

        if (!await db.AssetCategories.AnyAsync())
        {
            db.AssetCategories.AddRange(
                new AssetCategory { Name = "HVAC", NameAr = "تكييف الهواء" },
                new AssetCategory { Name = "Electrical", NameAr = "كهربائي" },
                new AssetCategory { Name = "IT Equipment", NameAr = "معدات تقنية المعلومات" });
            await db.SaveChangesAsync();
        }

        if (!await db.Vendors.AnyAsync())
        {
            db.Vendors.AddRange(
                new Vendor { Name = "Gulf HVAC Solutions", ContactName = "Ali Hassan", Phone = "+965 2234 5678", Email = "ali@gulfhvac.kw", Specialization = "AC Repair & Maintenance", Status = "Active" },
                new Vendor { Name = "Kuwait Cooling Systems", ContactName = "Khalid Mansour", Phone = "+965 2456 7890", Email = "k.mansour@kcs.kw", Specialization = "Industrial HVAC", Status = "Active" });
            await db.SaveChangesAsync();
        }

        // Idempotent "add what's missing by AssetTag" — tops up a partially-seeded DB, spread
        // across all 3 categories and every zone above so filters/maps have real variety to show.
        {
            var adminUser = await um.FindByEmailAsync("admin@shiftflow.com");
            var zonesByName = await db.Zones.ToDictionaryAsync(z => z.Name);
            var categoriesByName = await db.AssetCategories.Where(c => c.ParentCategoryId == null).ToDictionaryAsync(c => c.Name);

            if (adminUser != null && zonesByName.Count > 0 && categoriesByName.Count > 0)
            {
                var seedAssets = new (string Tag, string Name, string Category, string Zone, string? Model, string Serial, string Status)[]
                {
                    ("AST-0001", "Rooftop AC Unit 1",          "HVAC",        "Block 3 Substation",          "Carrier 50XC-24",   "SN-10021", "Working"),
                    ("AST-0002", "Rooftop AC Unit 2",          "HVAC",        "Block 3 Substation",          "Daikin VRV-IV",     "SN-10022", "Defective"),
                    ("AST-0003", "Split AC Unit",              "HVAC",        "Salmiya Substation",          "LG Multi-V",        "SN-10023", "Working"),
                    ("AST-0004", "Chiller Unit 1",              "HVAC",        "Fahaheel Pumping Station",    "Trane CVHF",        "SN-10024", "Maintenance"),
                    ("AST-0005", "Package AC Unit",             "HVAC",        "Khaitan Distribution Center", "York YZ",           "SN-10025", "Working"),
                    ("AST-0016", "Rooftop AC Unit 3",           "HVAC",        "Rumaithiya Substation",       "Carrier 50XC-24",   "SN-10036", "Working"),

                    ("AST-0006", "Main Distribution Panel",     "Electrical",  "Central Admin Building",      "Schneider PowerLogic", "SN-20006", "Working"),
                    ("AST-0007", "Backup Generator",            "Electrical",  "Salmiya Substation",          "Cummins C1100D5",   "SN-20007", "Working"),
                    ("AST-0008", "Transformer Unit 1",          "Electrical",  "Block 3 Substation",          "ABB Dry-Type",      "SN-20008", "Defective"),
                    ("AST-0009", "UPS System",                  "Electrical",  "North Control Center",        "APC Symmetra",      "SN-20009", "Working"),
                    ("AST-0010", "Circuit Breaker Panel",       "Electrical",  "Sabah Al-Salem Substation",   "Siemens Sentron",   "SN-20010", "Maintenance"),
                    ("AST-0017", "Submersible Pump Motor",      "Electrical",  "Sulaibiya Pumping Station",   "Grundfos SP",       "SN-20017", "Working"),
                    ("AST-0019", "Voltage Regulator",           "Electrical",  "Fahaheel Pumping Station",    "Eaton VR-3",        "SN-20019", "Maintenance"),

                    ("AST-0011", "Network Switch Core",         "IT Equipment","Central Admin Building",      "Cisco Catalyst 9300", "SN-30011", "Working"),
                    ("AST-0012", "Server Rack A1",               "IT Equipment","North Control Center",        "Dell PowerEdge R740", "SN-30012", "Working"),
                    ("AST-0013", "Firewall Appliance",          "IT Equipment","Shuwaikh Warehouse",          "Fortinet FortiGate", "SN-30013", "Working"),
                    ("AST-0014", "CCTV NVR System",             "IT Equipment","Jahra Water Treatment Plant", "Hikvision DS-96",   "SN-30014", "Retired"),
                    ("AST-0015", "Wireless Access Point",       "IT Equipment","Mangaf Control Room",         "Ubiquiti UniFi",    "SN-30015", "Defective"),
                    ("AST-0018", "Access Control Panel",        "IT Equipment","Khaitan Distribution Center", "HID VertX",         "SN-30018", "Working"),
                    ("AST-0020", "Router Core Switch B2",       "IT Equipment","Shuwaikh Warehouse",          "Cisco ISR 4451",    "SN-30020", "Working"),
                };

                var existingTags = (await db.Assets.Select(a => a.AssetTag).ToListAsync()).ToHashSet();
                var missingAssets = seedAssets
                    .Where(a => !existingTags.Contains(a.Tag) && categoriesByName.ContainsKey(a.Category) && zonesByName.ContainsKey(a.Zone))
                    .Select(a => new Asset
                    {
                        AssetTag = a.Tag, Name = a.Name, CategoryId = categoriesByName[a.Category].Id, ZoneId = zonesByName[a.Zone].Id,
                        Model = a.Model, SerialNumber = a.Serial, Status = a.Status, CreatedByUserId = adminUser.Id,
                    })
                    .ToList();
                if (missingAssets.Count > 0)
                {
                    db.Assets.AddRange(missingAssets);
                    await db.SaveChangesAsync();
                }
            }
        }

        {
            var categories = await db.AssetCategories.Where(c => c.ParentCategoryId == null).ToDictionaryAsync(c => c.Name);

            // Deactivate the old generic "Report Failure"/"Request Maintenance" action types on the
            // top-level categories — replaced below by a single, category/subcategory-specific failure
            // report type each. Soft-disable (IsActive=false) rather than delete: any WorkOrder created
            // earlier against these rows still has a valid FK, they just stop appearing as options for
            // new reports (AssetActionTypesController.ByCategory filters on IsActive).
            var staleActionTypeNames = new[] { "Report Failure", "Request Maintenance" };
            var topLevelCategoryIds = categories.Values.Select(c => c.Id).ToList();
            var staleActionTypes = await db.AssetActionTypes
                .Where(t => topLevelCategoryIds.Contains(t.CategoryId) && staleActionTypeNames.Contains(t.Name) && t.IsActive)
                .ToListAsync();
            foreach (var t in staleActionTypes) t.IsActive = false;
            if (staleActionTypes.Count > 0) await db.SaveChangesAsync();

            // Idempotent add-what's-missing by (ParentId, Name) — same pattern as Areas' fill-in.
            var subcategorySeed = new (string Parent, string Name, string NameAr)[]
            {
                ("HVAC", "Split Units", "وحدات مقسمة"),
                ("HVAC", "Chillers", "المبردات"),
                ("Electrical", "Transformers", "المحولات"),
                ("Electrical", "Generators", "المولدات"),
                ("Electrical", "Panels", "اللوحات الكهربائية"),
                ("IT Equipment", "Servers", "الخوادم"),
                ("IT Equipment", "Network Equipment", "معدات الشبكة"),
                ("IT Equipment", "Security Systems", "أنظمة الأمن"),
            };
            var existingSubcatKeys = (await db.AssetCategories.Where(c => c.ParentCategoryId != null)
                .Select(c => new { c.ParentCategoryId, c.Name }).ToListAsync())
                .Select(c => (ParentId: (int)c.ParentCategoryId!, c.Name)).ToHashSet();
            var missingSubcats = subcategorySeed
                .Where(s => categories.ContainsKey(s.Parent) && !existingSubcatKeys.Contains((categories[s.Parent].Id, s.Name)))
                .Select(s => new AssetCategory { Name = s.Name, NameAr = s.NameAr, ParentCategoryId = categories[s.Parent].Id })
                .ToList();
            if (missingSubcats.Count > 0)
            {
                db.AssetCategories.AddRange(missingSubcats);
                await db.SaveChangesAsync();
            }
            var subcategories = await db.AssetCategories.Where(c => c.ParentCategoryId != null)
                .ToDictionaryAsync(c => (c.ParentCategoryId!.Value, c.Name));

            // Idempotent per (CategoryId, Name) — adds the action type and any missing causes under
            // it without touching ones that already exist, so a partial/earlier run tops up cleanly.
            async Task<AssetActionType> UpsertActionTypeAsync(int categoryId, string name, string nameAr, (string Name, string NameAr)[] causes)
            {
                var actionType = await db.AssetActionTypes.FirstOrDefaultAsync(t => t.CategoryId == categoryId && t.Name == name);
                if (actionType is null)
                {
                    actionType = new AssetActionType { CategoryId = categoryId, Name = name, NameAr = nameAr };
                    db.AssetActionTypes.Add(actionType);
                    await db.SaveChangesAsync();
                }
                var existingCauseNames = (await db.AssetActionCauses.Where(c => c.ActionTypeId == actionType.Id).Select(c => c.Name).ToListAsync()).ToHashSet();
                var missingCauses = causes.Where(c => !existingCauseNames.Contains(c.Name))
                    .Select(c => new AssetActionCause { ActionTypeId = actionType.Id, Name = c.Name, NameAr = c.NameAr }).ToList();
                if (missingCauses.Count > 0)
                {
                    db.AssetActionCauses.AddRange(missingCauses);
                    await db.SaveChangesAsync();
                }
                return actionType;
            }

            // Top-level categories — one failure-report action type each, named for that category.
            if (categories.TryGetValue("HVAC", out var hvac))
                await UpsertActionTypeAsync(hvac.Id, "Report HVAC Failure", "الإبلاغ عن عطل التكييف",
                    [("Compressor Failure", "عطل الضاغط"), ("Refrigerant Leak", "تسرب غاز التبريد"), ("No Power", "لا يوجد طاقة")]);
            if (categories.TryGetValue("Electrical", out var electrical))
                await UpsertActionTypeAsync(electrical.Id, "Report Electrical Failure", "الإبلاغ عن عطل كهربائي",
                    [("Short Circuit", "دائرة قصر"), ("Overload", "حمل زائد"), ("No Power", "لا يوجد طاقة")]);
            if (categories.TryGetValue("IT Equipment", out var itEquipment))
                await UpsertActionTypeAsync(itEquipment.Id, "Report Hardware Failure", "الإبلاغ عن عطل في الجهاز",
                    [("Hardware Failure", "عطل في الجهاز"), ("Network Outage", "انقطاع الشبكة"), ("Overheating", "ارتفاع الحرارة")]);

            // Subcategories — each gets its own, more specific failure-report action type. The asset
            // picker's ByCategory lookup already includes the parent category's types too when a
            // subcategory is selected, so these are additive, not exclusive, alternatives.
            if (categories.TryGetValue("HVAC", out var hvacCat))
            {
                if (subcategories.TryGetValue((hvacCat.Id, "Split Units"), out var splitUnits))
                    await UpsertActionTypeAsync(splitUnits.Id, "Report Split Unit Failure", "الإبلاغ عن عطل الوحدة المقسمة",
                        [("Compressor Failure", "عطل الضاغط"), ("Refrigerant Leak", "تسرب غاز التبريد"), ("No Power", "لا يوجد طاقة")]);
                if (subcategories.TryGetValue((hvacCat.Id, "Chillers"), out var chillers))
                    await UpsertActionTypeAsync(chillers.Id, "Report Chiller Failure", "الإبلاغ عن عطل المبرد",
                        [("Compressor Failure", "عطل الضاغط"), ("Low Water Flow", "انخفاض تدفق المياه"), ("No Power", "لا يوجد طاقة")]);
            }
            if (categories.TryGetValue("Electrical", out var electricalCat))
            {
                if (subcategories.TryGetValue((electricalCat.Id, "Transformers"), out var transformers))
                    await UpsertActionTypeAsync(transformers.Id, "Report Transformer Failure", "الإبلاغ عن عطل المحول",
                        [("Overload", "حمل زائد"), ("Oil Leak", "تسرب الزيت"), ("No Power", "لا يوجد طاقة")]);
                if (subcategories.TryGetValue((electricalCat.Id, "Generators"), out var generators))
                    await UpsertActionTypeAsync(generators.Id, "Report Generator Failure", "الإبلاغ عن عطل المولد",
                        [("Fuel Issue", "مشكلة في الوقود"), ("Starting Failure", "فشل التشغيل"), ("No Power", "لا يوجد طاقة")]);
                if (subcategories.TryGetValue((electricalCat.Id, "Panels"), out var panels))
                    await UpsertActionTypeAsync(panels.Id, "Report Panel Failure", "الإبلاغ عن عطل اللوحة",
                        [("Short Circuit", "دائرة قصر"), ("Overload", "حمل زائد"), ("No Power", "لا يوجد طاقة")]);
            }
            if (categories.TryGetValue("IT Equipment", out var itEquipmentCat))
            {
                if (subcategories.TryGetValue((itEquipmentCat.Id, "Servers"), out var servers))
                    await UpsertActionTypeAsync(servers.Id, "Report Server Failure", "الإبلاغ عن عطل الخادم",
                        [("Hardware Failure", "عطل في الجهاز"), ("Overheating", "ارتفاع الحرارة"), ("No Power", "لا يوجد طاقة")]);
                if (subcategories.TryGetValue((itEquipmentCat.Id, "Network Equipment"), out var networkEquipment))
                    await UpsertActionTypeAsync(networkEquipment.Id, "Report Network Failure", "الإبلاغ عن عطل الشبكة",
                        [("Network Outage", "انقطاع الشبكة"), ("Hardware Failure", "عطل في الجهاز")]);
                if (subcategories.TryGetValue((itEquipmentCat.Id, "Security Systems"), out var securitySystems))
                    await UpsertActionTypeAsync(securitySystems.Id, "Report Security System Failure", "الإبلاغ عن عطل نظام الأمن",
                        [("Hardware Failure", "عطل في الجهاز"), ("Network Outage", "انقطاع الشبكة"), ("No Power", "لا يوجد طاقة")]);
            }

            await db.SaveChangesAsync();
        }

        if (!await db.Contracts.AnyAsync())
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Name == "Gulf HVAC Solutions");
            var asset1 = await db.Assets.FirstOrDefaultAsync(a => a.AssetTag == "AST-0001");
            if (vendor != null && asset1 != null)
            {
                var warranty = new Contract
                {
                    VendorId = vendor.Id, ContractType = "Warranty", ContractNumber = "CTR-2026-001",
                    StartDate = DateTime.UtcNow.AddMonths(-6), EndDate = DateTime.UtcNow.AddMonths(18),
                };
                warranty.AssetLinks.Add(new ContractAsset { AssetId = asset1.Id });
                db.Contracts.Add(warranty);

                // Service contract — the vendor-driven work order flow resolves its vendor from
                // this, not from Warranty/Purchase/Insurance contracts. Testable immediately after seeding.
                var service = new Contract
                {
                    VendorId = vendor.Id, ContractType = "Service", ContractNumber = "CTR-2026-002",
                    StartDate = DateTime.UtcNow.AddMonths(-3), EndDate = DateTime.UtcNow.AddMonths(9),
                };
                service.AssetLinks.Add(new ContractAsset { AssetId = asset1.Id });
                db.Contracts.Add(service);

                await db.SaveChangesAsync();
            }
        }

        // Demo vendor portal login — same "Gulf HVAC Solutions" vendor as the Service contract
        // above, so the whole employee-report → accept → vendor-fix flow is testable right after seeding.
        var gulfHvac = await db.Vendors.Include(v => v.User).FirstOrDefaultAsync(v => v.Name == "Gulf HVAC Solutions");
        if (gulfHvac != null && gulfHvac.UserId == null)
        {
            const string vendorEmail = "vendor@gulfhvac.kw";
            var vendorUser = await um.FindByEmailAsync(vendorEmail);
            if (vendorUser == null)
            {
                vendorUser = new ApplicationUser { UserName = vendorEmail, Email = vendorEmail, FullName = gulfHvac.Name, IsActive = true, EmailConfirmed = true };
                var r = await um.CreateAsync(vendorUser, "Vendor@123456");
                if (r.Succeeded) await um.AddToRoleAsync(vendorUser, "Vendor");
            }
            gulfHvac.UserId = vendorUser.Id;
            await db.SaveChangesAsync();
        }

        // Second demo vendor login — deliberately has no work orders assigned, so it exercises
        // the "vendor A cannot see vendor B's data" access-control boundary right after seeding.
        var kcs = await db.Vendors.Include(v => v.User).FirstOrDefaultAsync(v => v.Name == "Kuwait Cooling Systems");
        if (kcs != null && kcs.UserId == null)
        {
            const string vendor2Email = "vendor2@kcs.kw";
            var vendor2User = await um.FindByEmailAsync(vendor2Email);
            if (vendor2User == null)
            {
                vendor2User = new ApplicationUser { UserName = vendor2Email, Email = vendor2Email, FullName = kcs.Name, IsActive = true, EmailConfirmed = true };
                var r = await um.CreateAsync(vendor2User, "Vendor2@123456");
                if (r.Succeeded) await um.AddToRoleAsync(vendor2User, "Vendor");
            }
            kcs.UserId = vendor2User.Id;
            await db.SaveChangesAsync();
        }

        // Demo work order already "Sent to Vendor" for Gulf HVAC on AST-0001, so the vendor
        // portal, admin work-order list, and attachment-validation flows are all testable
        // immediately after seeding instead of depending on ad-hoc manual testing to create one.
        if (!await db.WorkOrders.AnyAsync(w => w.Notes == "PW-SEED-DEMO-WO"))
        {
            var demoAsset = await db.Assets.FirstOrDefaultAsync(a => a.AssetTag == "AST-0001");
            var demoActionType = await db.AssetActionTypes.Include(t => t.Causes)
                .FirstOrDefaultAsync(t => t.Name == "Report Failure" && t.Category!.Name == "HVAC");
            var demoVendor = await db.Vendors.FirstOrDefaultAsync(v => v.Name == "Gulf HVAC Solutions");
            var demoAdminUser = await um.FindByEmailAsync("admin@shiftflow.com");
            if (demoAsset != null && demoActionType != null && demoVendor != null && demoAdminUser != null)
            {
                var year = DateTime.UtcNow.Year;
                var seq = await db.WorkOrders.CountAsync(w => w.CreatedDate.Year == year) + 1;
                var demoWorkOrder = new WorkOrder
                {
                    WorkOrderNumber = $"WO-{year}-{seq:D4}", AssetId = demoAsset.Id, Priority = "Medium", Stage = "Sent to Vendor",
                    VendorId = demoVendor.Id, ActionTypeId = demoActionType.Id, CauseId = demoActionType.Causes.First().Id,
                    Description = "Seeded demo work order for Playwright/manual testing.", Notes = "PW-SEED-DEMO-WO",
                    CreatedByUserId = demoAdminUser.Id, CreatedDate = DateTime.UtcNow,
                };
                demoWorkOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "Draft", ChangedAt = DateTime.UtcNow, ChangedByUserId = demoAdminUser.Id });
                demoWorkOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "New", ChangedAt = DateTime.UtcNow, ChangedByUserId = demoAdminUser.Id });
                demoWorkOrder.StageEvents.Add(new WorkOrderStageEvent { Stage = "Sent to Vendor", ChangedAt = DateTime.UtcNow, ChangedByUserId = demoAdminUser.Id });
                db.WorkOrders.Add(demoWorkOrder);
                await db.SaveChangesAsync();
            }
        }

        if (!await db.WorkOrderBlockReasons.AnyAsync())
        {
            db.WorkOrderBlockReasons.AddRange(
                new WorkOrderBlockReason { Name = "Waiting on Parts", NameAr = "بانتظار القطع" },
                new WorkOrderBlockReason { Name = "Access Denied", NameAr = "تم رفض الوصول" },
                new WorkOrderBlockReason { Name = "Not Covered by Contract", NameAr = "غير مشمول بالعقد" },
                new WorkOrderBlockReason { Name = "Needs Site Visit", NameAr = "يحتاج زيارة ميدانية" });
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext db, RoleManager<ApplicationRole> rm)
    {
        var catalog = new[]
        {
            new Permission { Name = "User.View",          Category = "Users", Description = "See the list of user accounts, their roles, and profile details" },
            new Permission { Name = "User.Manage",        Category = "Users", Description = "Create new users, edit their details, assign roles, and deactivate or delete accounts" },

            // Inspection Orders
            new Permission { Name = "InspectionOrder.View",         Category = "Inspection Orders", Description = "See all inspection orders across the organization" },
            new Permission { Name = "InspectionOrder.Manage",       Category = "Inspection Orders", Description = "Create and cancel inspection orders" },
            new Permission { Name = "InspectionOrder.Report",       Category = "Inspection Orders", Description = "Report an asset's inspection outcome on an order assigned to you or your team" },
            new Permission { Name = "InspectionOrder.Export",       Category = "Inspection Orders", Description = "Export the inspection order list to Excel" },
            // Teams
            new Permission { Name = "Team.View",                    Category = "Teams", Description = "See the list of teams and their members" },
            new Permission { Name = "Team.Manage",                  Category = "Teams", Description = "Create teams and manage their membership" },
            // Locations
            new Permission { Name = "Location.Manage",              Category = "Locations", Description = "Add, edit, or remove physical site locations" },
            // AI Assistant
            new Permission { Name = "AiAssistant.Use",              Category = "AI Assistant", Description = "Access the AI assistant for inspection-order-related questions and actions" },
            // Administration
            new Permission { Name = "AuditLog.View",                Category = "Administration", Description = "See the history of who changed what across the system" },
            new Permission { Name = "Rbac.Manage",                  Category = "Administration", Description = "Create roles, assign permissions, and control what each user or role can access" },
            // Super permission
            new Permission { Name = "System.IsAdmin",               Category = "Administration", Description = "Grants every permission in the system, overriding all other settings — use with caution" },
            // Asset Management
            new Permission { Name = "Asset.View",                   Category = "Asset Management", Description = "See the asset register — tags, categories, locations, and status" },
            new Permission { Name = "Asset.Manage",                 Category = "Asset Management", Description = "Add, edit, or retire assets in the register" },
            new Permission { Name = "AssetCategory.Manage",         Category = "Asset Management", Description = "Create and edit asset categories" },
            new Permission { Name = "Vendor.View",                  Category = "Asset Management", Description = "See the list of maintenance vendors" },
            new Permission { Name = "Vendor.Manage",                Category = "Asset Management", Description = "Add, edit, or suspend maintenance vendors" },
            new Permission { Name = "WorkOrder.View",                Category = "Asset Management", Description = "See maintenance work orders and their status" },
            new Permission { Name = "WorkOrder.Manage",              Category = "Asset Management", Description = "Create work orders and advance them through their stages" },
            new Permission { Name = "WorkOrder.Assign",              Category = "Asset Management", Description = "Assign a vendor to a work order" },
            new Permission { Name = "WorkOrder.Export",              Category = "Asset Management", Description = "Export asset and work order lists to Excel or PDF" },
            new Permission { Name = "Contract.View",                 Category = "Asset Management", Description = "See vendor contracts and which assets they cover" },
            new Permission { Name = "Contract.Manage",               Category = "Asset Management", Description = "Create and edit vendor contracts and link them to assets" },
            new Permission { Name = "Asset.ReportAction",            Category = "Asset Management", Description = "Report a failure or other action on an asset, creating a draft work order for admin review" },
            new Permission { Name = "Asset.ScopeManage",             Category = "Asset Management", Description = "Restrict which zone, area, or category of assets a specific employee can see" },
        };

        var existing = await db.Permissions.ToListAsync();
        var existingByName = existing.ToDictionary(p => p.Name);
        var toAdd = catalog.Where(p => !existingByName.ContainsKey(p.Name)).ToList();
        if (toAdd.Count > 0)
        {
            db.Permissions.AddRange(toAdd);
        }

        // Keep display text in sync with the source-of-truth list above for permissions
        // seeded by an earlier version of this app (Description/Category are pure display
        // data, not a structural change, so no migration is needed — just resync on boot).
        var changed = false;
        foreach (var perm in catalog)
        {
            if (!existingByName.TryGetValue(perm.Name, out var row)) continue;
            if (row.Description != perm.Description) { row.Description = perm.Description; changed = true; }
            if (row.Category != perm.Category) { row.Category = perm.Category; changed = true; }
        }

        if (toAdd.Count > 0 || changed)
            await db.SaveChangesAsync();

        var matrix = new Dictionary<string, string[]>
        {
            ["Admin"] =
            [
                "System.IsAdmin",
                "User.Manage", "User.View",
                "InspectionOrder.View", "InspectionOrder.Manage", "InspectionOrder.Report", "InspectionOrder.Export",
                "Team.View", "Team.Manage",
                "AiAssistant.Use",
                "AuditLog.View", "Location.Manage", "Rbac.Manage",
                "Asset.View", "Asset.Manage", "AssetCategory.Manage", "Asset.ScopeManage", "Asset.ReportAction",
                "Vendor.View", "Vendor.Manage",
                "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign", "WorkOrder.Export",
                "Contract.View", "Contract.Manage",
            ],
            ["OperationsManager"] =
            [
                "User.View",
                "InspectionOrder.View", "InspectionOrder.Manage", "InspectionOrder.Report", "InspectionOrder.Export",
                "Team.View", "Team.Manage",
                "AiAssistant.Use",
                "Asset.View", "Asset.Manage", "Asset.ScopeManage", "Asset.ReportAction",
                "Vendor.View", "Vendor.Manage",
                "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign", "WorkOrder.Export",
                "Contract.View", "Contract.Manage",
            ],
            ["Supervisor"] =
            [
                "InspectionOrder.View", "InspectionOrder.Manage", "InspectionOrder.Report",
                "Team.View",
                "Asset.View", "Asset.Manage", "Asset.ScopeManage", "Asset.ReportAction",
                "Vendor.View",
                "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign", "WorkOrder.Export",
                "Contract.View",
            ],
            ["Section Head"] =
            [
                "InspectionOrder.View", "Team.View",
                "Asset.View", "Vendor.View", "WorkOrder.View",
            ],
            ["Senior Engineer"] =
            [
                "InspectionOrder.View", "InspectionOrder.Report",
                "Asset.View", "Asset.ReportAction", "WorkOrder.View", "WorkOrder.Manage",
            ],
            ["Engineer"] =
            [
                "InspectionOrder.Report",
                "Asset.View", "Asset.ReportAction", "WorkOrder.View", "WorkOrder.Manage",
            ],
            ["Operation Engineer"] =
            [
                "InspectionOrder.Report",
                "Asset.View", "Asset.ReportAction", "WorkOrder.View", "WorkOrder.Manage",
            ],
            ["Technician"] =
            [
                "InspectionOrder.Report",
                "Asset.View", "Asset.ReportAction", "WorkOrder.View", "WorkOrder.Manage",
            ],
            ["HR"] =
            [
                "Asset.View",
            ],
        };

        foreach (var (roleName, perms) in matrix)
        {
            var role = await rm.FindByNameAsync(roleName);
            if (role is null) continue;

            foreach (var perm in perms)
            {
                var exists = await db.RolePermissions
                    .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionName == perm);
                if (!exists)
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionName = perm });
            }
        }

        // Shift.Manage was removed from the catalog — it only ever gated the dead/
        // unlinked legacy ShiftsController, which has since been deleted. Clean up
        // any grants + the Permission catalog row from earlier seeded runs.
        var staleShiftManageGrants = await db.RolePermissions
            .Where(rp => rp.PermissionName == "Shift.Manage")
            .ToListAsync();
        if (staleShiftManageGrants.Count > 0)
            db.RolePermissions.RemoveRange(staleShiftManageGrants);
        var staleShiftManagePermission = await db.Permissions
            .Where(p => p.Name == "Shift.Manage")
            .ToListAsync();
        if (staleShiftManagePermission.Count > 0)
            db.Permissions.RemoveRange(staleShiftManagePermission);

        // The entire shift-scheduling/rostering subsystem (Schedule.*, Group.Member.Manage,
        // ChangeRequest.*, ShiftOps.*, ShiftAnalytics.View) was retired in favor of Inspection
        // Orders/Teams. Clean up every grant, override, and catalog row left over from earlier
        // seeded runs — same pattern as the other stale-permission cleanups in this method.
        var retiredShiftPermissionPrefixes = new[] { "Schedule.", "Group.Member.", "ChangeRequest.", "ShiftOps.", "ShiftAnalytics." };
        var allPermissionNames = await db.Permissions.Select(p => p.Name).ToListAsync();
        var retiredShiftPermissions = allPermissionNames
            .Where(n => retiredShiftPermissionPrefixes.Any(n.StartsWith))
            .ToList();
        if (retiredShiftPermissions.Count > 0)
        {
            var staleGrants2 = await db.RolePermissions.Where(rp => retiredShiftPermissions.Contains(rp.PermissionName)).ToListAsync();
            if (staleGrants2.Count > 0) db.RolePermissions.RemoveRange(staleGrants2);
            var staleOverrides2 = await db.UserPermissions.Where(up => retiredShiftPermissions.Contains(up.PermissionName)).ToListAsync();
            if (staleOverrides2.Count > 0) db.UserPermissions.RemoveRange(staleOverrides2);
            var stalePerms2 = await db.Permissions.Where(p => retiredShiftPermissions.Contains(p.Name)).ToListAsync();
            if (stalePerms2.Count > 0) db.Permissions.RemoveRange(stalePerms2);
        }

        // "IsAdmin" (no dot) was briefly seeded before being renamed to "System.IsAdmin"
        // to fit the Category.Verb convention the permission views rely on for display
        // (perm.Name.Split('.')[1] throws on a name with no dot). Clean up the stale row.
        var staleIsAdminGrants = await db.RolePermissions
            .Where(rp => rp.PermissionName == "IsAdmin")
            .ToListAsync();
        if (staleIsAdminGrants.Count > 0)
            db.RolePermissions.RemoveRange(staleIsAdminGrants);
        var staleIsAdminOverrides = await db.UserPermissions
            .Where(up => up.PermissionName == "IsAdmin")
            .ToListAsync();
        if (staleIsAdminOverrides.Count > 0)
            db.UserPermissions.RemoveRange(staleIsAdminOverrides);
        var staleIsAdminPermission = await db.Permissions
            .Where(p => p.Name == "IsAdmin")
            .ToListAsync();
        if (staleIsAdminPermission.Count > 0)
            db.Permissions.RemoveRange(staleIsAdminPermission);

        // Report.View / Report.Export were retired along with the standalone Reports
        // page (its one chart worth keeping — Incidents by Severity — moved to the
        // Executive Dashboard). Clean up any grants, overrides, and the Permission
        // catalog rows from earlier seeded runs, same pattern as Shift.Manage above.
        foreach (var retiredPermission in new[] { "Report.View", "Report.Export" })
        {
            var staleGrants = await db.RolePermissions
                .Where(rp => rp.PermissionName == retiredPermission)
                .ToListAsync();
            if (staleGrants.Count > 0)
                db.RolePermissions.RemoveRange(staleGrants);
            var staleOverrides = await db.UserPermissions
                .Where(up => up.PermissionName == retiredPermission)
                .ToListAsync();
            if (staleOverrides.Count > 0)
                db.UserPermissions.RemoveRange(staleOverrides);
            var stalePermission = await db.Permissions
                .Where(p => p.Name == retiredPermission)
                .ToListAsync();
            if (stalePermission.Count > 0)
                db.Permissions.RemoveRange(stalePermission);
        }

        // User.View is now actually policy-enforced (Users.Profile) — trim grants back
        // to exactly the roles the hardcoded check it replaced already allowed, so
        // wiring it up doesn't silently widen access for roles that were seeded earlier
        // before this enforcement existed.
        var userViewAllowedRoles = new HashSet<string> { "Admin", "OperationsManager" };
        var staleUserViewGrants = await db.RolePermissions
            .Where(rp => rp.PermissionName == "User.View")
            .ToListAsync();
        foreach (var grant in staleUserViewGrants)
        {
            var roleName = (await rm.FindByIdAsync(grant.RoleId))?.Name;
            if (roleName != null && !userViewAllowedRoles.Contains(roleName))
                db.RolePermissions.Remove(grant);
        }

        await db.SaveChangesAsync();
    }
}
