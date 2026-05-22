using System;
using System.Collections.Generic;
using System.Linq;
using TodoList.Client.Models;
using TodoList.Client.Storage;
using VideoOS.Platform.Client;

namespace TodoList.Client
{
    public class TodoListViewItemManager : ViewItemManager
    {
        private const string TitleKey = "Title";
        private const string FontSizeKey = "FontSize";
        private const string ModeKey = "Mode";
        private const string FreeTasksJsonKey = "FreeTasksJson";
        private const string TemplatesJsonKey = "TemplatesJson";
        private const string ActiveTemplateIdKey = "ActiveTemplateId";
        private const string TemplateProgressJsonKey = "TemplateProgressJson";
        private const string DefaultTemplateIdKey = "DefaultTemplateId";

        public const string ModeFree = "Free";
        public const string ModeTemplate = "Template";

        public event EventHandler ConfigurationChanged;

        public TodoListViewItemManager()
            : base("TodoListViewItemManager")
        {
        }

        public string Title
        {
            get => GetProperty(TitleKey) ?? string.Empty;
            set => SetProperty(TitleKey, value ?? string.Empty);
        }

        public string FontSize
        {
            get => GetProperty(FontSizeKey) ?? "14";
            set => SetProperty(FontSizeKey, value ?? "14");
        }

        public string Mode
        {
            get
            {
                var mode = GetProperty(ModeKey);
                return mode == ModeTemplate ? ModeTemplate : ModeFree;
            }
            set => SetProperty(ModeKey, value == ModeTemplate ? ModeTemplate : ModeFree);
        }

        public string ActiveTemplateId
        {
            get => GetProperty(ActiveTemplateIdKey) ?? string.Empty;
            set => SetProperty(ActiveTemplateIdKey, value ?? string.Empty);
        }

        public string DefaultTemplateId
        {
            get => GetProperty(DefaultTemplateIdKey) ?? string.Empty;
            set => SetProperty(DefaultTemplateIdKey, value ?? string.Empty);
        }

        public List<TodoTask> GetFreeTasks()
        {
            return TodoStorage.ReadFreeTasks(GetProperty(FreeTasksJsonKey));
        }

        public void SetFreeTasks(List<TodoTask> tasks)
        {
            SetProperty(FreeTasksJsonKey, TodoStorage.WriteFreeTasks(tasks));
        }

        public List<TodoTemplate> GetTemplates()
        {
            return TodoStorage.ReadTemplates(GetProperty(TemplatesJsonKey));
        }

        public void SetTemplates(List<TodoTemplate> templates)
        {
            SetProperty(TemplatesJsonKey, TodoStorage.WriteTemplates(templates));
        }

        public Dictionary<string, bool> GetTemplateProgress()
        {
            return TodoStorage.ReadProgress(GetProperty(TemplateProgressJsonKey));
        }

        public void SetTemplateProgress(Dictionary<string, bool> progress)
        {
            SetProperty(TemplateProgressJsonKey, TodoStorage.WriteProgress(progress));
        }

        public TodoTemplate GetActiveTemplate()
        {
            var id = ActiveTemplateId;
            if (string.IsNullOrEmpty(id))
                return null;
            return GetTemplates().FirstOrDefault(t => t.Id == id);
        }

        public TodoTemplate GetDefaultTemplate()
        {
            var id = DefaultTemplateId;
            if (string.IsNullOrEmpty(id))
                return null;
            return GetTemplates().FirstOrDefault(t => t.Id == id);
        }

        public void Save()
        {
            SaveProperties();
        }

        public void RaiseConfigurationChanged()
        {
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }

        public override void PropertiesLoaded()
        {
        }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new TodoListViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new TodoListPropertiesWpfUserControl(this);
        }
    }
}
