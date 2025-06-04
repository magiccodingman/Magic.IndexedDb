using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb;

public class DbMigrationInstruction
{
    public string Action { get; set; }
    public string StoreName { get; set; }
    public string Details { get; set; }
}