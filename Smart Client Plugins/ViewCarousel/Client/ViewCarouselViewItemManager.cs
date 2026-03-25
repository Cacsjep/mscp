using System.Collections.Generic;
using System.Text.Json;
using VideoOS.Platform.Client;

namespace ViewCarousel.Client
{
    public class ViewCarouselViewItemManager : ViewItemManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public ViewCarouselViewItemManager() : base("ViewCarouselViewItemManager") { }

        public string DefaultTime
        {
            get => GetProperty("DefaultTime") ?? "10";
            set => SetProperty("DefaultTime", value);
        }

        public List<CarouselViewEntry> GetViewEntryList()
        {
            var json = GetProperty("ViewEntries");
            if (string.IsNullOrEmpty(json))
                return new List<CarouselViewEntry>();
            try
            {
                return JsonSerializer.Deserialize<List<CarouselViewEntry>>(json, JsonOptions)
                       ?? new List<CarouselViewEntry>();
            }
            catch
            {
                return new List<CarouselViewEntry>();
            }
        }

        public void SetViewEntryList(List<CarouselViewEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            SetProperty("ViewEntries", json);
        }

        public void Save() => SaveProperties();

        public override void PropertiesLoaded() { }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
            => new ViewCarouselViewItemWpfUserControl(this);

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
            => new ViewCarouselPropertiesWpfUserControl(this);
    }
}
