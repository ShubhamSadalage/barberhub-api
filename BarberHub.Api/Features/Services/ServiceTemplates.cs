namespace BarberHub.Api.Features.Services;

public static class ServiceTemplates
{
    public static readonly List<(string Name, string Category, decimal Price, int Duration, string Description)> All = new()
    {
        // Hair
        ("Hair Cut",             "Hair",   150, 30,  "Classic haircut with scissor or clipper finish."),
        ("Hair Cut + Wash",      "Hair",   200, 45,  "Haircut with a relaxing shampoo wash."),
        ("Hair Styling",         "Hair",   100, 20,  "Blow-dry and style — no cut."),
        ("Hair Colour",          "Hair",   500, 60,  "Full hair colour treatment."),
        ("Hair Highlights",      "Hair",   700, 90,  "Partial or full highlights."),
        ("Keratin Treatment",    "Hair",  1200,120,  "Smooth, frizz-free hair treatment."),

        // Beard
        ("Beard Trim",           "Beard",  100, 20,  "Neat beard shaping and trimming."),
        ("Beard Trim + Shave",   "Beard",  150, 30,  "Beard trim with a clean razor shave."),
        ("Clean Shave",          "Beard",  100, 20,  "Full razor shave with hot towel."),
        ("Beard Colour",         "Beard",  200, 30,  "Beard colouring and conditioning."),

        // Combo
        ("Hair Cut + Beard Trim","Combo",  220, 50,  "Full hair and beard grooming combo."),
        ("Grooming Package",     "Combo",  350, 75,  "Hair, beard, and face care combined."),

        // Kids
        ("Kids Hair Cut",        "Kids",   100, 25,  "Gentle haircut for children under 12."),

        // Face & Skin
        ("Face Clean Up",        "Skin",   250, 30,  "Deep facial cleansing and scrub."),
        ("D-Tan Face Pack",      "Skin",   300, 40,  "De-tanning pack for brighter skin."),
        ("Head Massage",         "Skin",   200, 30,  "Relaxing scalp and head massage."),
    };
}
