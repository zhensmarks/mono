import re

filepath = '/home/abeng/BMachine.v2/src/BMachine.App/Views/MainWindow.axaml'
with open(filepath, 'r') as f:
    content = f.read()

# Replace Auto, Auto with Auto, *
content = content.replace('<Grid RowDefinitions="Auto, Auto">', '<Grid RowDefinitions="Auto, *">')

# Wrap StackPanel with ScrollViewer
# Find: <StackPanel Grid.Row="1" Spacing="0" Margin="0,10,0,0">
stackpanel_pattern = r'<StackPanel Grid\.Row="1" Spacing="0" Margin="0,10,0,0">'
stackpanel_replacement = r'<ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">\n                        <StackPanel Spacing="0" Margin="0,10,0,0">'
content = re.sub(stackpanel_pattern, stackpanel_replacement, content)

# The end of the StackPanel looks like this:
#                             </Button>
#                         </StackPanel>
#                     </StackPanel>
#                 </Grid>
#             </Border>

# Wait, let's just do a text replacement for the end
end_pattern = r'                            </Button>\n                        </StackPanel>\n                    </StackPanel>\n                </Grid>'
end_replacement = r'                            </Button>\n                        </StackPanel>\n                    </StackPanel>\n                    </ScrollViewer>\n                </Grid>'

content = re.sub(end_pattern, end_replacement, content)

with open(filepath, 'w') as f:
    f.write(content)
