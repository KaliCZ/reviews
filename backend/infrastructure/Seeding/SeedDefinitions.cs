using Reviews.Infrastructure.Entities;

namespace Reviews.Infrastructure.Seeding;

// Pure data — no IO, no DI. The seed runner consumes these, uploads any
// referenced images to blob storage, and inserts via DbContext.
//
// The mix is hand-curated so per-product averages are obviously different:
// products 1, 4, 6, 9 average ~4.5+; products 3, 5, 10 average ~1.7-2.3;
// the rest sit in the middle. Useful for showing the rating UI in real states.
internal static class SeedDefinitions
{
    // Eight stable author IDs so re-runs are deterministic and demos are easy
    // to reason about. These map to the OIDC `sub` of seeded test users (or
    // are just orphan UUIDs in dev if those test users don't exist yet).
    public static readonly Guid Alice = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob   = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carol = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Dave  = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Eve   = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Frank = new("66666666-6666-6666-6666-666666666666");
    public static readonly Guid Grace = new("77777777-7777-7777-7777-777777777777");
    public static readonly Guid Henry = new("88888888-8888-8888-8888-888888888888");

    public static IEnumerable<Product> Products() =>
    [
        new(1,  "sony-wh-1000xm5",         "Sony WH-1000XM5 Wireless Headphones", "Flagship over-ear ANC headphones with industry-leading noise cancellation and 30-hour battery life.", ProductImage("sony-wh")),
        new(2,  "usb-c-cable-pack",        "USB-C Cable 3-Pack (1m)",             "Three braided USB-C to USB-C cables, 60W PD, 480Mbps data.",                                       ProductImage("usbc-cable")),
        new(3,  "acme-smartwatch",         "Acme Pro Smartwatch",                 "Fitness tracking, heart-rate monitoring, GPS, and a 1.4-inch AMOLED display.",                     ProductImage("acme-watch")),
        new(4,  "logi-mx-master-3s",       "Logitech MX Master 3S",               "Ultra-quiet clicks, 8K-DPI sensor, MagSpeed scroll wheel, multi-device pairing.",                  ProductImage("logi-mx")),
        new(5,  "boombox-mini",            "BoomBox Mini Bluetooth Speaker",      "Compact portable speaker with 6-hour battery and IPX5 splash resistance.",                         ProductImage("boombox")),
        new(6,  "ipad-air-11",             "iPad Air 11\" (M3, 256GB)",           "M3 chip, Liquid Retina display, Apple Pencil Pro support.",                                        ProductImage("ipad-air")),
        new(7,  "xyz-mechanical-keyboard", "XYZ Mechanical Keyboard (Brown switches)", "Hot-swappable 75% layout, PBT keycaps, USB-C and Bluetooth.",                                 ProductImage("mechkeyb")),
        new(8,  "travelpro-tripod",        "TravelPro Phone Tripod",              "Aluminum tripod with phone mount and Bluetooth shutter remote.",                                   ProductImage("tripod")),
        new(9,  "single-origin-coffee",    "Highland Single-Origin Coffee 1kg",   "Whole-bean Ethiopia Yirgacheffe, light roast, brewed within 14 days of roasting.",                 ProductImage("coffee")),
        new(10, "powerjuice-10000",        "PowerJuice 10000 Mini Power Bank",    "10,000mAh USB-C PD power bank with passthrough charging.",                                         ProductImage("powerbank")),
    ];

    // Raw seed data; the Seeder turns image-seed strings into Azurite blob
    // URLs and constructs the entity via Review.CreateSeed. CreatedAt offsets
    // give the list a natural order under sort=newest.
    public record SeedReviewData(
        long ProductId,
        Guid AuthorId,
        string AuthorName,
        short Rating,
        string Title,
        string Body,
        int Score,
        DateTime CreatedAt,
        IReadOnlyList<string> ImageSeeds);

    public static IEnumerable<SeedReviewData> Reviews()
    {
        var now = DateTime.UtcNow;
        DateTime daysAgo(int n) => now.AddDays(-n);

        // Product 1: Sony WH-1000XM5 — premium, ~4.6 average
        yield return New(1, Alice, "Alice", 5, "Best ANC on the market", "Tried Bose and AirPods Max — the Sony beats both for noise cancelling and the call mics are surprisingly usable.", daysAgo(40), score: 14);
        yield return New(1, Bob,   "Bob",   5, "Worth every penny",      "Battery actually lasts the full week of commutes. Carry case is small enough to fit in a laptop bag pocket.",       daysAgo(32), score:  9, "sony-r1");
        yield return New(1, Carol, "Carol", 4, "Comfortable, slight clamping force", "Great pair, only quibble is the clamping force on bigger heads — eased after a week of use.",          daysAgo(20), score:  6);
        yield return New(1, Dave,  "Dave",  5, "Replaced my XM4",        "Sound is noticeably warmer than the XM4 and the touch controls are easier to learn.",                              daysAgo(12), score:  4, "sony-r2", "sony-r3");
        yield return New(1, Eve,   "Eve",   4, "App is meh",             "Headphones are great. The Sony Headphones Connect app keeps demanding updates and re-pairings, which is annoying.", daysAgo(5),  score:  3);

        // Product 2: USB-C cable pack — mixed, ~3.4 average
        yield return New(2, Frank, "Frank", 5, "Cheap and they work",     "Three of three deliver full 60W to my MacBook. Braiding feels nice.",                                              daysAgo(60), score:  3);
        yield return New(2, Grace, "Grace", 3, "Fine, not great",         "Two work fine, one is finicky and only charges in one orientation. For the price, fine.",                          daysAgo(50), score:  2);
        yield return New(2, Henry, "Henry", 2, "One died after a month",  "First cable stopped passing data after about a month. Other two still going.",                                     daysAgo(30), score:  1, "usbc-r1");
        yield return New(2, Alice, "Alice", 4, "Decent backups",          "Not premium build but works for keeping spares in bags and at desks.",                                             daysAgo(15), score:  1);
        yield return New(2, Bob,   "Bob",   3, "Average",                  "They charge things. What more do you want?",                                                                       daysAgo(6),  score:  0);

        // Product 3: Acme smartwatch — bad, ~1.8 average
        yield return New(3, Carol, "Carol", 1, "Battery dies in a day",   "Advertised 7-day battery; in reality I get one day with notifications on. Returned mine.",                          daysAgo(45), score: 12, "acme-r1");
        yield return New(3, Dave,  "Dave",  2, "GPS is useless",           "GPS lock takes 3-4 minutes outdoors. By then I'm halfway through my run.",                                          daysAgo(35), score:  8);
        yield return New(3, Eve,   "Eve",   1, "App lost my data",         "Updated the companion app and lost three months of workouts. No backup option visible anywhere.",                  daysAgo(25), score:  7);
        yield return New(3, Frank, "Frank", 3, "Screen is nice at least",  "Display is genuinely sharp. Everything else about it is bargain-bin.",                                              daysAgo(14), score:  3);
        yield return New(3, Grace, "Grace", 2, "Heart rate jumps around",  "Heart rate readings during exercise swing 30bpm in a single minute when nothing has changed. Useless for training.", daysAgo(8),  score:  4);

        // Product 4: Logitech MX Master 3S — excellent, ~4.75
        yield return New(4, Henry, "Henry", 5, "Best mouse I've owned",     "Switched from MX Master 2S — quiet click is genuinely quieter, scroll wheel is unchanged (good).",                  daysAgo(55), score: 11);
        yield return New(4, Alice, "Alice", 5, "Multi-device flow",         "Pairing across two laptops + iPad with Flow is the killer feature. Saves me a real five minutes a day.",            daysAgo(40), score:  8, "logi-r1");
        yield return New(4, Bob,   "Bob",   4, "Heavy but in a good way",   "Heavier than my old mouse — turns out I prefer it. Build feels long-lasting.",                                       daysAgo(22), score:  5);
        yield return New(4, Carol, "Carol", 5, "Worth the upgrade",         "The horizontal scroll on the side wheel finally has a real use in spreadsheets.",                                    daysAgo(10), score:  4);

        // Product 5: BoomBox Mini — bad, ~1.5
        yield return New(5, Dave,  "Dave",  1, "Sounds like a tin can",     "Bass is non-existent and anything above mid volume distorts.",                                                      daysAgo(50), score:  9, "boombox-r1");
        yield return New(5, Eve,   "Eve",   2, "Battery is honest at least", "Battery does last the advertised 6 hours. Unfortunately you'll want to turn it off after one.",                    daysAgo(38), score:  6);
        yield return New(5, Frank, "Frank", 2, "BT pairing flaky",          "Drops connection every time someone walks between me and the phone. Five feet away.",                                daysAgo(20), score:  4);
        yield return New(5, Grace, "Grace", 1, "Returned",                  "Volume rocker started intermittently triggering by itself. Returned within a week.",                                  daysAgo(7),  score:  3);

        // Product 6: iPad Air 11" — good, ~4.5
        yield return New(6, Henry, "Henry", 5, "Hits the sweet spot",       "Pro features I actually use, none of the Pro pricing.",                                                              daysAgo(60), score:  7);
        yield return New(6, Alice, "Alice", 4, "Pencil Pro is great",       "Pencil Pro hover and squeeze gestures speed up note-taking real well. Display only 60Hz which I notice scrolling.",   daysAgo(35), score:  5);
        yield return New(6, Bob,   "Bob",   5, "Use it more than my laptop now", "M3 makes Procreate and Affinity Photo feel desktop-class.",                                                       daysAgo(12), score:  4, "ipad-r1");
        yield return New(6, Carol, "Carol", 4, "Wish it had ProMotion",     "The 60Hz screen is the only place this still feels behind the Pro.",                                                  daysAgo(4),  score:  3);

        // Product 7: XYZ Mechanical Keyboard — mixed/good, ~4.0
        yield return New(7, Dave,  "Dave",  5, "Hot-swap is the move",      "Bought it stock with browns, swapped to silent reds in five minutes without solder. Build is metal where it counts.", daysAgo(48), score:  6, "keyb-r1");
        yield return New(7, Eve,   "Eve",   3, "Software is rough",         "Hardware is great. The companion software for layers and lighting is a Windows-only mess.",                            daysAgo(30), score:  4);
        yield return New(7, Frank, "Frank", 4, "Solid daily driver",        "PBT keycaps, real USB-C, real Bluetooth multi-host. Three out of three checked.",                                       daysAgo(15), score:  3);
        yield return New(7, Grace, "Grace", 4, "Browns are a bit mushy",    "As advertised — they're not bad, just less tactile than I'd hoped. Hot-swap saved this from being a return.",          daysAgo(6),  score:  2);

        // Product 8: Phone tripod — mixed, ~3.0
        yield return New(8, Henry, "Henry", 3, "Fine for the price",         "Light, folds small, holds my phone. Bluetooth shutter pairs once and forgets you exist by next session.",            daysAgo(40), score:  3);
        yield return New(8, Alice, "Alice", 4, "Good travel option",         "Goes in a backpack pocket. Stable enough for stationary photo, less so for video pans.",                              daysAgo(22), score:  2, "tripod-r1");
        yield return New(8, Bob,   "Bob",   2, "Phone clamp slips",          "With a heavier phone (Pro Max), the clamp creeps backwards over a few minutes and the phone tilts.",                   daysAgo(11), score:  4);

        // Product 9: Coffee — good, ~4.75
        yield return New(9, Carol, "Carol", 5, "Real fresh",                  "Roast date stamped on the bag was four days before delivery. Tastes like it.",                                       daysAgo(50), score:  5);
        yield return New(9, Dave,  "Dave",  5, "Bright, fruity, exactly as described", "Yirgacheffe done well. Pulls beautiful as espresso, also great as filter.",                                  daysAgo(28), score:  4, "coffee-r1");
        yield return New(9, Eve,   "Eve",   4, "A bit pricey",                 "It's good — fairly priced for single-origin but not a bargain.",                                                      daysAgo(16), score:  2);
        yield return New(9, Frank, "Frank", 5, "Will buy again",               "Haven't had a bag this consistent since my last trip to a roaster.",                                                  daysAgo(7),  score:  3);

        // Product 10: Power bank — bad, ~2.0
        yield return New(10, Grace, "Grace", 1, "Lies about capacity",         "Charges my phone less than two full times. 10,000mAh? Sure.",                                                          daysAgo(42), score: 10, "power-r1");
        yield return New(10, Henry, "Henry", 2, "Charges itself slowly",       "Takes 7+ hours to recharge from empty over USB-C. Not the worst, but not the \"fast\" they advertise.",                 daysAgo(30), score:  6);
        yield return New(10, Alice, "Alice", 3, "OK if you keep expectations low", "Fine as an emergency thing in a bag. Don't expect to fast-charge a laptop.",                                       daysAgo(18), score:  3);
        yield return New(10, Bob,   "Bob",   2, "Got hot",                     "Got uncomfortably hot mid-charge a couple of times. Stopped using it.",                                                 daysAgo(5),  score:  5);
    }

    private static SeedReviewData New(
        long productId, Guid authorId, string authorName, short rating,
        string title, string body, DateTime createdAt, int score, params string[] imageSeeds) =>
        new(productId, authorId, authorName, rating, title, body, score, createdAt, imageSeeds);

    private static string ProductImage(string seed) => $"/api/images/seed/{seed}.jpg";
}
