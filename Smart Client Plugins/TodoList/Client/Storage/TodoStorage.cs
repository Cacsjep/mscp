using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TodoList.Client.Models;

namespace TodoList.Client.Storage
{
    public static class TodoStorage
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        public static string NewId() => Guid.NewGuid().ToString("N");

        public static List<TodoTask> ReadFreeTasks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<TodoTask>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<TodoTask>>(json, Settings);
                if (list == null)
                    return new List<TodoTask>();

                foreach (var t in list)
                {
                    if (string.IsNullOrEmpty(t.Id))
                        t.Id = NewId();
                    if (t.Text == null)
                        t.Text = string.Empty;
                }
                return list;
            }
            catch
            {
                return new List<TodoTask>();
            }
        }

        public static string WriteFreeTasks(List<TodoTask> tasks)
        {
            return JsonConvert.SerializeObject(tasks ?? new List<TodoTask>(), Settings);
        }

        public static List<TodoTemplate> ReadTemplates(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<TodoTemplate>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<TodoTemplate>>(json, Settings);
                if (list == null)
                    return new List<TodoTemplate>();

                foreach (var tmpl in list)
                {
                    if (string.IsNullOrEmpty(tmpl.Id))
                        tmpl.Id = NewId();
                    if (tmpl.Name == null)
                        tmpl.Name = string.Empty;
                    if (tmpl.Tasks == null)
                        tmpl.Tasks = new List<TodoTemplateTask>();
                    foreach (var t in tmpl.Tasks)
                    {
                        if (string.IsNullOrEmpty(t.Id))
                            t.Id = NewId();
                        if (t.Text == null)
                            t.Text = string.Empty;
                    }
                }
                return list;
            }
            catch
            {
                return new List<TodoTemplate>();
            }
        }

        public static string WriteTemplates(List<TodoTemplate> templates)
        {
            return JsonConvert.SerializeObject(templates ?? new List<TodoTemplate>(), Settings);
        }

        public static Dictionary<string, bool> ReadProgress(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, bool>();

            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json, Settings);
                return dict ?? new Dictionary<string, bool>();
            }
            catch
            {
                return new Dictionary<string, bool>();
            }
        }

        public static string WriteProgress(Dictionary<string, bool> progress)
        {
            return JsonConvert.SerializeObject(progress ?? new Dictionary<string, bool>(), Settings);
        }
    }
}
