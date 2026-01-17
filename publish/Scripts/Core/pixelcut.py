import requests, os, time, traceback, logging

class Pixelcut:

    def __init__(self, **kwargs):
        self.__dict__.update(kwargs)

    def __getattr__(self, attr):
        return getattr(self.parent, attr)

    def _process(self, message):
        self.is_running()
        self.signals.process.emit({
            'id': self.payload['id'],
            'item': self.payload['item'],
            'data': {
                'status': message
            },
        })

    def _rendered(self):
        self.signals.rendered.emit({
            'id': self.payload['id'],
            'item': self.payload['item'],
            'data': {
                'rendered': self.payload['output'],
                'status': '100%' # for clear complete
            },
        })

    def run(self):
        try:
            if self.payload['job'] == 'upscale':
                self.upscale()
            elif self.payload['job'] == 'remove_bg':
                self.remove_bg()
            else:
                return self._process(f'ERROR: no job')

        except Exception as e:
            traceback.print_exc()
            return self._process(f'ERROR: {str(e)}')

        # before set to finish
        # check first file exist or not
        if os.path.exists(self.payload['output']):
            self._rendered()
            self._process('100%')
        else:
            self._process('ERROR: output not found')

    def upscale(self):
        headers = {
            'accept': 'application/json, text/plain, */*',
            'origin': 'https://www.pixelcut.ai',
            'referer': 'https://www.pixelcut.ai/',
            'user-agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36',
            'x-client-version': 'web',
        }

        with open(self.payload['file'], 'rb') as f:
            files = {
                'image': f.read(),
            }

            self._process('request to pixecut...')
            response = requests.post('https://api2.pixelcut.app/image/upscale/v1', headers=headers, data={
                'scale': '2',
            }, files=files)

            self._process('save image...')
            with open(self.payload['output'], 'wb') as result_file:
                result_file.write(response.content)
            f.close()

    def remove_bg(self):
        headers = {
            'accept': 'application/json, text/plain, */*',
            'accept-language': 'en-US,en;q=0.9,id;q=0.8',
            'user-agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36',
            'x-client-version': 'web',
            'x-locale': 'en',
        }

        with open(self.payload['file'], 'rb') as f:
            files = {
                'image': f.read(),
            }

            self._process('request to pixecut...')

            response = requests.post('https://api2.pixelcut.app/image/matte/v1', headers=headers, data={
                'format': 'png',
                'model': 'v1',
            }, files=files)

            self._process('save image...')
            self.payload['output'] = os.path.join(self.payload['output_path'], f"{self.payload['name']}.png")
            with open(self.payload['output'], 'wb') as result_file:
                result_file.write(response.content)
                result_file.close()

            f.close()

    def stop(self):
        print('stop for pixelcut...')