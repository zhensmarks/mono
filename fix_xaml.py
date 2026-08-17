import re

def fix_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    # 1. Remove LetterSpacing from Button tags
    content = re.sub(r'(<Button[^>]*?)\s+LetterSpacing="[^"]*"([^>]*?>)', r'\1\2', content)

    # 2. Fix Grid Padding="12" with Background="#1AFFFFFF"
    grid_str = '<Grid ColumnDefinitions="Auto, *, Auto" Background="#1AFFFFFF" Padding="12">'
    grid_replacement = '<Border Background="#1AFFFFFF" Padding="12">\n                                            <Grid ColumnDefinitions="Auto, *, Auto">'
    
    content = content.replace(grid_str, grid_replacement)
    
    # And we must close the border after the Grid closes.
    # The grid has a path icon, text block, and button inside it.
    # It ends with </Grid>
    # The pattern in the template:
    # <Grid ...>
    #   <PathIcon .../>
    #   <TextBlock .../>
    #   <Button ...>...</Button>
    # </Grid>
    # Let's replace the matching </Grid> for this specific Grid.
    
    # Actually it's easier to just do regex for the whole Grid block.
    pattern = r'<Grid ColumnDefinitions="Auto, \*, Auto" Background="#1AFFFFFF" Padding="12">(.*?)</Grid>'
    replacement = r'<Border Background="#1AFFFFFF" Padding="12">\n                                            <Grid ColumnDefinitions="Auto, *, Auto">\1</Grid>\n                                        </Border>'
    
    # Reset content to original, because we just string replaced the start tag
    with open(filepath, 'r') as f:
        content = f.read()
        
    content = re.sub(r'(<Button[^>]*?)\s+LetterSpacing="[^"]*"([^>]*?>)', r'\1\2', content)
    content = re.sub(pattern, replacement, content, flags=re.DOTALL)
    
    with open(filepath, 'w') as f:
        f.write(content)

fix_file('/home/abeng/BMachine.v2/src/BMachine.UI/Views/EditingCardListView.axaml')
fix_file('/home/abeng/BMachine.v2/src/BMachine.UI/Views/LateCardListView.axaml')
