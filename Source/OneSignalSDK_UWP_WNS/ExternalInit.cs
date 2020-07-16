using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneSignalSDK_WP_WNS {
   public interface ExternalInit {
      string sdkType { get; }
      string GetAppArguments();
   }
}
