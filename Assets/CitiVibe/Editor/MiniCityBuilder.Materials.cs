using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        /// <summary>London plane bark: mottled olive-grey and cream patches where the outer bark flakes away.</summary>
        static void PlaneBark(float u, float v, out Color c, out float h)
        {
            float patches = Fbm(u, v, 81, 4, 4);
            float fine = Grain(u, v, 82);
            var grey = new Color(0.38f, 0.36f, 0.3f);
            var olive = new Color(0.45f, 0.44f, 0.32f);
            var cream = new Color(0.78f, 0.74f, 0.6f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.56f, patches));
            c = Color.Lerp(Color.Lerp(grey, olive, Fbm(u, v, 83, 8, 3)), cream, k) * (0.9f + 0.15f * fine);
            h = k * 0.6f + fine * 0.4f;
        }

        /// <summary>Weathered tar-and-gravel roofing with ponding stains.</summary>
        static void RoofGravel(float u, float v, out Color c, out float h)
        {
            float stain = Fbm(u, v, 91, 3, 4);
            float pebble = Grain(u, v, 92);
            float g = 0.32f + 0.12f * stain + 0.1f * pebble;
            c = new Color(g, g * 0.98f, g * 0.94f);
            h = pebble;
        }

        sealed class Mats
        {
            static Shader lit;
            public Material brownstone, limestone, granite, concrete, asphalt, roof, grass, gravel, plinth, hedge, bark,
                lineWhite, bikeGreen, frameWhite, frameDark, cornice, iron, door, storeGlass, officeGlass, spandrel, aluminium,
                loftPaint, wood, water, voidBlack, soil, lampPole, lampHead, lampLens, globeGreen, signalHousing,
                lensRed, lensAmber, lensGreen, lensWalk, lensStop, hydrant, hydrantCap, mailbox, basket, tire, rim, chrome,
                carGlass, trimBlack, headlight, taillight, headlightOff, taillightOff, plate, taxi, taxiSign, interior,
                cargo, busBody, busStripe, bagBlack, bagWhite, acWhite, galvanized, glassPanel, kiosk, tankWood, mapPanel,
                litWarm, litCool, curtained, steamOrange;
            public Material[] bricks, glass, leaves, carPaint, awnings, signBands, skin, shirts, pants, shoes, hair, accents;

            public Mats()
            {
                lit = Shader.Find("Universal Render Pipeline/Lit");
                var redTex = MakeTexture("BrickRed", Brick(new Color(0.55f, 0.22f, 0.15f), new Color(0.68f, 0.32f, 0.22f), new Color(0.72f, 0.69f, 0.63f), 1), 3f);
                var buffTex = MakeTexture("BrickBuff", Brick(new Color(0.74f, 0.6f, 0.43f), new Color(0.84f, 0.71f, 0.53f), new Color(0.78f, 0.75f, 0.68f), 2), 3f);
                var brownTex = MakeTexture("BrickBrown", Brick(new Color(0.34f, 0.2f, 0.14f), new Color(0.45f, 0.27f, 0.18f), new Color(0.6f, 0.57f, 0.52f), 3), 3f);
                var brownstoneTex = MakeTexture("Brownstone", Stone(2, 4, 1.2f, 0.6f, new Color(0.42f, 0.29f, 0.22f), 0.12f, 4), 2.5f);
                var limestoneTex = MakeTexture("Limestone", Stone(2, 4, 1.5f, 0.75f, new Color(0.83f, 0.8f, 0.72f), 0.08f, 5), 2f);
                var concreteTex = MakeTexture("Concrete", Concrete, 2.5f);
                var asphaltTex = MakeTexture("Asphalt", Asphalt, 2f);
                var grassTex = MakeTexture("Grass", Grass, 2f);
                var gravelTex = MakeTexture("Gravel", Gravel, 3f);
                var barkTex = MakeTexture("PlaneBark", PlaneBark, 2f);
                var roofTex = MakeTexture("RoofGravel", RoofGravel, 2f);

                var brick = new Vector2(1.68f, 1.64f);
                var red = Lit("Brick Red", Color.white, 0.18f, 0f, redTex, brick);
                var buff = Lit("Brick Buff", Color.white, 0.18f, 0f, buffTex, brick);
                var brown = Lit("Brick Brown", Color.white, 0.18f, 0f, brownTex, brick);
                bricks = new[]
                {
                    red, buff, brown,
                    Tint(red, "Brick Red Dark", new Color(0.82f, 0.78f, 0.78f)),
                    Tint(red, "Brick Red Orange", new Color(1.08f, 0.96f, 0.86f)),
                    Tint(buff, "Brick Buff Grey", new Color(0.86f, 0.88f, 0.92f)),
                    Tint(buff, "Brick Cream", new Color(1.12f, 1.08f, 1.02f)),
                    Tint(brown, "Brick Brown Warm", new Color(1.15f, 1.02f, 0.92f)),
                };
                brownstone = Lit("Brownstone", Color.white, 0.22f, 0f, brownstoneTex, new Vector2(2.4f, 2.4f));
                limestone = Lit("Limestone", Color.white, 0.25f, 0f, limestoneTex, new Vector2(3f, 3f));
                granite = Lit("Granite Curb", new Color(0.78f, 0.78f, 0.8f), 0.3f, 0f, limestoneTex, new Vector2(1.5f, 1.5f));
                concrete = Lit("Sidewalk Concrete", Color.white, 0.2f, 0f, concreteTex, new Vector2(3.04f, 3.04f));
                asphalt = Lit("Asphalt", Color.white, 0.3f, 0f, asphaltTex, new Vector2(8f, 8f));
                roof = Lit("Roof Gravel", Color.white, 0.15f, 0f, roofTex, new Vector2(4f, 4f));
                grass = Lit("Lawn", Color.white, 0.1f, 0f, grassTex, new Vector2(4f, 4f));
                gravel = Lit("Park Gravel", Color.white, 0.12f, 0f, gravelTex, new Vector2(3f, 3f));
                plinth = Lit("Diorama Plinth", new Color(0.2f, 0.19f, 0.18f), 0.35f);
                hedge = Lit("Hedge", new Color(0.55f, 0.75f, 0.5f), 0.15f, 0f, grassTex, new Vector2(1.2f, 1.2f));
                bark = Lit("Plane Bark", Color.white, 0.15f, 0f, barkTex, new Vector2(1f, 1f));

                lineWhite = Lit("Paint White", new Color(0.9f, 0.9f, 0.87f), 0.35f);
                bikeGreen = Lit("Bike Lane Green", new Color(0.24f, 0.5f, 0.32f), 0.3f, 0f, asphaltTex, new Vector2(4f, 4f));
                bikeGreen.SetColor("_BaseColor", new Color(1.7f, 3.0f, 1.9f));
                frameWhite = Lit("Window Frame Cream", new Color(0.88f, 0.85f, 0.78f), 0.5f);
                frameDark = Lit("Window Frame Dark", new Color(0.09f, 0.11f, 0.1f), 0.45f);
                cornice = Lit("Cornice Metal", new Color(0.2f, 0.2f, 0.19f), 0.35f, 0.2f);
                iron = Lit("Black Iron", new Color(0.05f, 0.05f, 0.05f), 0.4f, 0.5f);
                door = Lit("Door Wood", new Color(0.3f, 0.16f, 0.09f), 0.45f);
                storeGlass = Glow("Storefront Glass", new Color(0.13f, 0.17f, 0.19f), Color.black, 0.95f, 0.3f);
                officeGlass = Glow("Office Glass", new Color(0.2f, 0.29f, 0.34f), Color.black, 0.96f, 0.4f);
                spandrel = Lit("Office Spandrel", new Color(0.16f, 0.18f, 0.2f), 0.6f, 0.6f);
                aluminium = Lit("Aluminium Mullion", new Color(0.62f, 0.64f, 0.66f), 0.7f, 0.8f);
                loftPaint = Lit("Cast Iron Cream", new Color(0.84f, 0.8f, 0.7f), 0.45f, 0.1f);
                awnings = new[]
                {
                    Lit("Awning Green", new Color(0.07f, 0.28f, 0.19f), 0.2f), Lit("Awning Red", new Color(0.52f, 0.09f, 0.07f), 0.2f),
                    Lit("Awning Navy", new Color(0.09f, 0.13f, 0.28f), 0.2f), Lit("Awning Black", new Color(0.05f, 0.05f, 0.05f), 0.25f),
                    Lit("Awning Burgundy", new Color(0.3f, 0.06f, 0.1f), 0.2f),
                };
                signBands = new[]
                {
                    Lit("Sign Band Charcoal", new Color(0.11f, 0.12f, 0.13f), 0.4f), Lit("Sign Band Cream", new Color(0.82f, 0.78f, 0.66f), 0.4f),
                    Lit("Sign Band Teal", new Color(0.1f, 0.34f, 0.36f), 0.4f), Lit("Sign Band Oxblood", new Color(0.36f, 0.08f, 0.07f), 0.4f),
                };
                wood = Lit("Weathered Cedar", new Color(0.42f, 0.31f, 0.21f), 0.2f);
                water = Lit("Fountain Water", new Color(0.18f, 0.33f, 0.36f), 0.97f, 0.1f);
                voidBlack = Lit("Stairwell Dark", new Color(0.02f, 0.02f, 0.02f), 0f);
                soil = Lit("Tree Pit Soil", new Color(0.18f, 0.13f, 0.09f), 0.05f);
                lampPole = Lit("Pole Silver", new Color(0.46f, 0.48f, 0.47f), 0.55f, 0.7f);
                lampHead = Lit("Lamp Head", new Color(0.2f, 0.21f, 0.21f), 0.5f, 0.5f);
                lampLens = Glow("Lamp Lens", new Color(0.9f, 0.88f, 0.8f), Color.black, 0.8f);
                globeGreen = Glow("Subway Globe", new Color(0.2f, 0.7f, 0.35f), new Color(0.15f, 0.9f, 0.35f) * 0.6f);
                signalHousing = Lit("Signal Housing", new Color(0.11f, 0.13f, 0.11f), 0.4f);
                lensRed = Glow("Lens Red", new Color(0.22f, 0.04f, 0.03f), Color.black, 0.85f);
                lensAmber = Glow("Lens Amber", new Color(0.25f, 0.17f, 0.05f), Color.black, 0.85f);
                lensGreen = Glow("Lens Green", new Color(0.03f, 0.18f, 0.1f), Color.black, 0.85f);
                lensWalk = Glow("Lens Walk", new Color(0.2f, 0.2f, 0.2f), Color.black, 0.8f);
                lensStop = Glow("Lens Stop", new Color(0.25f, 0.12f, 0.04f), Color.black, 0.8f);
                hydrant = Lit("Hydrant Red", new Color(0.6f, 0.1f, 0.07f), 0.5f, 0.2f);
                hydrantCap = Lit("Hydrant Silver", new Color(0.72f, 0.72f, 0.7f), 0.55f, 0.6f);
                mailbox = Lit("Mailbox Blue", new Color(0.08f, 0.2f, 0.45f), 0.55f, 0.2f);
                basket = Lit("Litter Basket Green", new Color(0.1f, 0.22f, 0.14f), 0.4f, 0.4f);
                tire = Lit("Tire Rubber", new Color(0.035f, 0.035f, 0.035f), 0.3f);
                rim = Lit("Wheel Rim", new Color(0.55f, 0.56f, 0.58f), 0.75f, 0.9f);
                chrome = Lit("Chrome", new Color(0.8f, 0.8f, 0.8f), 0.9f, 1f);
                carGlass = Lit("Car Glass", new Color(0.05f, 0.07f, 0.09f), 0.97f, 0.5f);
                trimBlack = Lit("Black Plastic Trim", new Color(0.04f, 0.04f, 0.045f), 0.35f);
                headlight = Glow("Headlight", new Color(0.95f, 0.95f, 0.9f), new Color(0.3f, 0.3f, 0.28f), 0.9f);
                taillight = Glow("Taillight", new Color(0.6f, 0.05f, 0.04f), new Color(0.3f, 0.01f, 0.01f), 0.9f);
                headlightOff = Lit("Headlight Parked", new Color(0.85f, 0.86f, 0.84f), 0.9f, 0.3f);
                taillightOff = Lit("Taillight Parked", new Color(0.45f, 0.04f, 0.03f), 0.85f);
                plate = Lit("Licence Plate", new Color(0.9f, 0.88f, 0.75f), 0.4f);
                taxi = Lit("Taxi Yellow", new Color(0.95f, 0.7f, 0.08f), 0.82f, 0.25f);
                taxiSign = Glow("Taxi Roof Sign", new Color(0.95f, 0.93f, 0.85f), new Color(0.3f, 0.28f, 0.2f), 0.6f);
                interior = Lit("Vehicle Interior", new Color(0.08f, 0.08f, 0.08f), 0.2f);
                cargo = Lit("Cargo Box White", new Color(0.86f, 0.86f, 0.84f), 0.4f, 0.1f);
                busBody = Lit("Bus Body", new Color(0.84f, 0.86f, 0.88f), 0.7f, 0.2f);
                busStripe = Lit("Bus Stripe Blue", new Color(0.08f, 0.3f, 0.65f), 0.6f);
                bagBlack = Lit("Trash Bag Black", new Color(0.03f, 0.03f, 0.035f), 0.75f);
                bagWhite = Lit("Trash Bag White", new Color(0.82f, 0.82f, 0.8f), 0.6f);
                acWhite = Lit("AC Unit White", new Color(0.82f, 0.81f, 0.77f), 0.45f, 0.1f);
                galvanized = Lit("Galvanized Steel", new Color(0.56f, 0.58f, 0.58f), 0.5f, 0.8f);
                glassPanel = Transparent("Shelter Glass", new Color(0.7f, 0.8f, 0.82f, 0.28f));
                kiosk = Lit("Kiosk Green", new Color(0.08f, 0.3f, 0.2f), 0.5f, 0.3f);
                tankWood = Lit("Water Tank Cedar", new Color(0.5f, 0.38f, 0.27f), 0.15f, 0f, barkTex, new Vector2(0.5f, 2f));
                tankWood.SetColor("_BaseColor", new Color(0.75f, 0.62f, 0.55f));
                mapPanel = Glow("Route Map Panel", new Color(0.82f, 0.82f, 0.78f), Color.black, 0.5f);
                steamOrange = Lit("Steam Stack Orange", new Color(0.92f, 0.38f, 0.08f), 0.5f);

                glass = new[]
                {
                    Lit("Window Glass A", new Color(0.09f, 0.12f, 0.15f), 0.94f, 0.25f),
                    Lit("Window Glass B", new Color(0.15f, 0.19f, 0.21f), 0.92f, 0.2f),
                };
                curtained = Lit("Window Glass Curtained", new Color(0.42f, 0.36f, 0.28f), 0.85f, 0.05f);
                litWarm = Glow("Window Lit Warm", new Color(0.16f, 0.15f, 0.13f), Color.black, 0.9f, 0.15f);
                litCool = Glow("Window Lit Cool", new Color(0.12f, 0.15f, 0.17f), Color.black, 0.9f, 0.2f);
                leaves = new[]
                {
                    Lit("Leaves Deep", new Color(0.55f, 0.72f, 0.5f), 0.12f, 0f, grassTex, new Vector2(0.25f, 0.25f)),
                    Lit("Leaves Mid", new Color(0.72f, 0.88f, 0.55f), 0.12f, 0f, grassTex, new Vector2(0.25f, 0.25f)),
                    Lit("Leaves Olive", new Color(0.82f, 0.85f, 0.5f), 0.12f, 0f, grassTex, new Vector2(0.25f, 0.25f)),
                };
                Color[] paints =
                {
                    new(0.72f, 0.73f, 0.75f), new(0.06f, 0.07f, 0.08f), new(0.88f, 0.88f, 0.86f), new(0.1f, 0.17f, 0.33f),
                    new(0.42f, 0.07f, 0.06f), new(0.3f, 0.34f, 0.31f), new(0.52f, 0.49f, 0.43f), new(0.2f, 0.22f, 0.25f),
                    new(0.95f, 0.95f, 0.94f), new(0.35f, 0.38f, 0.42f),
                };
                carPaint = Palette("Car Paint", paints, 0.84f, 0.45f);
                skin = Palette("Skin", new Color[] { new(0.96f, 0.8f, 0.69f), new(0.87f, 0.67f, 0.52f), new(0.72f, 0.52f, 0.38f), new(0.55f, 0.37f, 0.25f), new(0.38f, 0.25f, 0.17f), new(0.26f, 0.17f, 0.12f) }, 0.35f, 0f);
                shirts = Palette("Shirt", new Color[] { new(0.15f, 0.2f, 0.32f), new(0.75f, 0.74f, 0.7f), new(0.5f, 0.12f, 0.1f), new(0.2f, 0.35f, 0.25f), new(0.08f, 0.08f, 0.09f), new(0.55f, 0.45f, 0.3f), new(0.3f, 0.45f, 0.6f), new(0.7f, 0.55f, 0.15f), new(0.45f, 0.25f, 0.4f), new(0.85f, 0.83f, 0.78f), new(0.35f, 0.33f, 0.3f), new(0.12f, 0.3f, 0.45f) }, 0.2f, 0f);
                pants = Palette("Pants", new Color[] { new(0.12f, 0.15f, 0.22f), new(0.08f, 0.08f, 0.09f), new(0.32f, 0.3f, 0.25f), new(0.45f, 0.4f, 0.32f), new(0.25f, 0.33f, 0.45f), new(0.2f, 0.2f, 0.22f) }, 0.2f, 0f);
                shoes = Palette("Shoes", new Color[] { new(0.05f, 0.04f, 0.04f), new(0.85f, 0.85f, 0.83f), new(0.3f, 0.18f, 0.1f), new(0.15f, 0.15f, 0.17f) }, 0.4f, 0f);
                hair = Palette("Hair", new Color[] { new(0.06f, 0.05f, 0.04f), new(0.22f, 0.13f, 0.07f), new(0.45f, 0.3f, 0.15f), new(0.7f, 0.6f, 0.4f), new(0.6f, 0.6f, 0.6f) }, 0.35f, 0f);
                accents = Palette("Accent", new Color[] { new(0.5f, 0.12f, 0.08f), new(0.12f, 0.12f, 0.13f), new(0.3f, 0.2f, 0.12f), new(0.15f, 0.3f, 0.5f), new(0.75f, 0.6f, 0.2f), new(0.4f, 0.45f, 0.35f) }, 0.3f, 0f);
            }

            Material[] Palette(string prefix, Color[] colors, float smoothness, float metallic)
            {
                var result = new Material[colors.Length];
                for (int i = 0; i < colors.Length; i++) result[i] = Lit($"{prefix} {i + 1}", colors[i], smoothness, metallic);
                return result;
            }

            static Material Lit(string name, Color color, float smoothness, float metallic = 0f,
                (Texture2D albedo, Texture2D normal) maps = default, Vector2 tileMetres = default)
            {
                var m = new Material(lit) { name = name, enableInstancing = true };
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", metallic);
                if (maps.albedo != null)
                {
                    m.SetTexture("_BaseMap", maps.albedo);
                    m.SetTexture("_BumpMap", maps.normal);
                    m.SetFloat("_BumpScale", 1f);
                    m.EnableKeyword("_NORMALMAP");
                    m.SetTextureScale("_BaseMap", new Vector2(1f / tileMetres.x, 1f / tileMetres.y));
                }
                AssetDatabase.CreateAsset(m, $"{Gen}/Materials/{name}.mat");
                return m;
            }

            static Material Tint(Material source, string name, Color tint)
            {
                var m = new Material(source) { name = name };
                m.SetColor("_BaseColor", tint);
                AssetDatabase.CreateAsset(m, $"{Gen}/Materials/{name}.mat");
                return m;
            }

            static Material Glow(string name, Color color, Color emission, float smoothness = 0.7f, float metallic = 0f)
            {
                var m = Lit(name, color, smoothness, metallic);
                m.SetColor("_EmissionColor", emission);
                m.EnableKeyword("_EMISSION");
                // URP re-derives _EMISSION from these flags on load; without an emissive flag the keyword
                // (and its shader variant in player builds) is dropped, so night glows never appear.
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(m);
                return m;
            }

            static Material Transparent(string name, Color color)
            {
                var m = Lit(name, color, 0.95f);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(m);
                return m;
            }

            /// <summary>Blender material names (Tools/Blender/citivibe_models.py) and their URP replacements.</summary>
            public Dictionary<string, Material> ModelRemap() => new()
            {
                ["Paint"] = carPaint[0], ["Glass"] = carGlass, ["Tire"] = tire, ["Rim"] = rim, ["Chrome"] = chrome, ["Trim"] = trimBlack,
                ["Headlight"] = headlight, ["Taillight"] = taillight, ["Plate"] = plate, ["TaxiSign"] = taxiSign, ["Interior"] = interior,
                ["CargoBox"] = cargo, ["BusBody"] = busBody, ["BusStripe"] = busStripe,
                ["Skin"] = skin[0], ["Shirt"] = shirts[0], ["Pants"] = pants[0], ["Shoes"] = shoes[0], ["Hair"] = hair[0], ["Accent"] = accents[0],
                ["Bark"] = bark, ["LeafA"] = leaves[0], ["LeafB"] = leaves[1], ["LeafC"] = leaves[2],
                ["Iron"] = iron, ["PoleSilver"] = lampPole, ["LampLens"] = lampLens, ["LampHead"] = lampHead, ["SignalBody"] = signalHousing,
                ["LensRed"] = lensRed, ["LensAmber"] = lensAmber, ["LensGreen"] = lensGreen, ["LensWalk"] = lensWalk, ["LensStop"] = lensStop,
                ["HydrantBody"] = hydrant, ["HydrantCap"] = hydrantCap, ["MailBlue"] = mailbox, ["Wood"] = wood, ["BasketGreen"] = basket,
                ["BagBlack"] = bagBlack, ["BagWhite"] = bagWhite, ["AcWhite"] = acWhite, ["Galvanized"] = galvanized, ["GlassPanel"] = glassPanel,
                ["KioskGreen"] = kiosk, ["TankWood"] = tankWood, ["MapPanel"] = mapPanel, ["Concrete"] = concrete,
            };
        }
    }
}
