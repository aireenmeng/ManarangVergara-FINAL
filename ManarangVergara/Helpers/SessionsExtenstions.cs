using System.Text.Json;

namespace ManarangVergara.Helpers
{
    // HELPER: ADDS NEW POWERS TO THE SESSION "BACKPACK"
    // by default, sessions can only store simple text or numbers.
    // this tool lets us store complex things (like a whole shopping cart) by turning them into text first.
    public static class SessionExtensions
    {
        // FUNCTION: PACK AN ITEM (SAVE TO SESSION)
        // "serialize" means turning a complicated object into a simple text string (json) so the session is allowed to hold it.
        public static void SetObjectAsJson(this ISession session, string key, object value)
        {
            session.SetString(key, JsonSerializer.Serialize(value));
        }

        // FUNCTION: UNPACK AN ITEM (GET FROM SESSION)
        // "deserialize" means taking that text string we saved earlier and turning it back into a real, usable code object.
        public static T? GetObjectFromJson<T>(this ISession session, string key)
        {
            var value = session.GetString(key);
            // if nothing is found, return nothing (default). otherwise, rebuild the object from the text.
            return value == null ? default : JsonSerializer.Deserialize<T>(value);
        }
    }
}