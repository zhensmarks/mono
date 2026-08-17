import re

filepath = '/home/abeng/BMachine.v2/src/BMachine.UI/Controls/StatWidget.axaml'
with open(filepath, 'r') as f:
    content = f.read()

# Find the Grid inside the Border
grid_pattern = r'<Border Padding="6,8,6,6" Background="Transparent">\s*<Grid>'
replacement = r'<Border Padding="6,8,6,6" Background="Transparent">\n                <Viewbox Stretch="Uniform" StretchDirection="DownOnly">\n                <Grid>'

content = re.sub(grid_pattern, replacement, content)

# Find the closing tag of that Grid. It's the last Grid before </Border> in the file.
# The structure is:
#                </Grid>
#             </Border>
#         </Button>

content = content.replace('                </Grid>\n            </Border>\n        </Button>',
                          '                </Grid>\n                </Viewbox>\n            </Border>\n        </Button>')

with open(filepath, 'w') as f:
    f.write(content)
