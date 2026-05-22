using System.Collections.Generic;

namespace TodoList.Client.Models
{
    public class TodoTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<TodoTemplateTask> Tasks { get; set; } = new List<TodoTemplateTask>();
    }
}
