using Microsoft.Playwright;
using System.Threading.Tasks;

namespace NoteFlow.Studio.Service.Providers
{
    public interface IPlaywrightProvider
    {
        Task<IBrowser> GetBrowserAsync();
        Task<IBrowser> GetHeadedBrowserAsync();
    }
}