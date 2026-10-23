import { withThemeByClassName } from '@storybook/addon-themes';
import './styles.css';

export default {
    decorators: [withThemeByClassName({ themes: { light: 'light', dark: 'dark' }, defaultTheme: 'dark' })],
    parameters: {
        backgrounds: { disable: true },
        // Accessibility violations fail the story tests (nx test-storybook audio-ui) and Chromatic.
        a11y: { test: 'error' }
    },
    tags: ['autodocs']
};
