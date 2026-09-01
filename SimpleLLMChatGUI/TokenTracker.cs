using System.Windows.Controls;

namespace SimpleLLMChatGUI
{
    internal class TokenTracker
    {
        private readonly TextBlock _target;
        private int _tokens;

        public TokenTracker(TextBlock target)
        {
            _target = target;
            Refresh();
        }

        public void SetTokens(int tokens)
        {
            _tokens = tokens < 0 ? 0 : tokens;
            Refresh();
        }

        public void Reset()
        {
            _tokens = 0;
            Refresh();
        }

        public void Refresh()
        {
            int contextWindowSize = App.Config != null
                ? App.Config.GetConfigInt("contextWindowSize", 0)
                : 0;

            int? max = contextWindowSize > 0 ? (int?)contextWindowSize : null;
            if (_target != null)
                _target.Text = TokenEstimator.FormatStatus(_tokens, max);
        }
    }
}
