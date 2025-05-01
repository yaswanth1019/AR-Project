using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARLocation.MapboxRoutes
{
    using Vendor.SimpleJSON;

    [Serializable]
    public class GeocodingFeature
    {
        public string text;
        public string place_name;
        public float relevance;
        public Route.Geometry geometry;

        public static GeocodingFeature Parse(JSONNode n)
        {
            var result = new GeocodingFeature();

            // Access the "properties" object
            var properties = n["properties"];


            if (properties != null)
            {
                result.text = properties["name"]; // Get 'name' instead of 'text'
                string name = properties["name"];
                string fullAddress = properties["full_address"];

                result.place_name = $"{name}, {fullAddress}";
            }

            if (n.HasKey("relevance"))
                result.relevance = n["relevance"].AsFloat;

            if (n.HasKey("geometry"))
                result.geometry = Route.Geometry.Parse(n["geometry"]);

            return result;
        }

        public override string ToString()
        {
            return $"GeocodingFeature{{ text = {text}, place_name = {place_name}, relevance = {relevance}, geometry = {geometry} }}";
        }
    }

    [Serializable]
    public class GeocodingResponse
    {
        public List<GeocodingFeature> features = new List<GeocodingFeature>();

        public static GeocodingResponse Parse(string s)
        {
            return Parse(JSON.Parse(s));
        }

        public static GeocodingResponse Parse(JSONNode n)
        {
            var result = new GeocodingResponse { };

            var features = n["features"].AsArray;

            int featureCount = features.Count; // Get the count of features

            Debug.Log("Number of responses: " + featureCount);


            foreach (var f in features)
            {
                result.features.Add(GeocodingFeature.Parse(f));
            }

            return result;
        }

        public override string ToString()
        {
            return $"Geocoding {{ features = [{string.Join(", ", features)}] }}";
        }
    }
}
