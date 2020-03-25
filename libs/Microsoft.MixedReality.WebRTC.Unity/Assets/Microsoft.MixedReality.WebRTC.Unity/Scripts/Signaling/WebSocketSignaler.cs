// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using UnityEngine;
using WebSocketSharp;

namespace Microsoft.MixedReality.WebRTC.Unity
{
    /// Simple signaler for debug and testing.
    /// This is based on https://github.com/bengreenier/node-dss and SHOULD NOT BE USED FOR PRODUCTION.
    /// </summary>
    [AddComponentMenu("MixedReality-WebRTC/WebSocket Signaler")]
    public class WebSocketSignaler : Signaler
    {
        [Serializable]
        public struct MessageHeader
        {
            public string type;
        }

        [Serializable]
        public struct IceCandidate
        {
            public string type;
            public string candidate;
            public int sdpMLineindex;
            public string sdpMid;
        }

        [Serializable]
        public struct Offer
        {
            public string type;
            public string offer;
        }

        [Serializable]
        public struct Answer
        {
            public string type;
            public string answer;
        }

        /// <summary>
        /// Automatically log all errors to the Unity console.
        /// </summary>
        [Tooltip("Automatically log all errors to the Unity console")]
        public bool AutoLogErrors = true;

        /// <summary>
        /// The https://github.com/bengreenier/node-dss HTTP service address to connect to
        /// </summary>
        [Header("Server")]
        [Tooltip("The websocket server to connect to")]
        public string WebsocketServerAddress = "ws://127.0.0.1:8081/";

        /// <summary>
        /// Websocket networking layer
        /// </summary>
        private WebSocket _ws;

        #region ISignaler interface

        public override bool SupportsRawMessages => true;

        /// <inheritdoc/>
        public override Task SendMessageAsync(Message message)
        {
            throw new NotImplementedException("Use Raw Messages instead");
        }

        #endregion


        private void OnEnable()
        {
            if (string.IsNullOrEmpty(WebsocketServerAddress))
            {
                enabled = false;
                throw new ArgumentNullException("HttpServerAddress");
            }
            if (!WebsocketServerAddress.EndsWith("/"))
            {
                WebsocketServerAddress += "/";
            }

            _ws = new WebSocket(WebsocketServerAddress);
            _ws.OnOpen += OnOpen;
            _ws.OnMessage += OnNewMessage;
            _ws.OnClose += OnClose;
            _ws.OnError += OnError;
            _ws.Connect();
        }

        private void OnDisable()
        {
            _ws.OnOpen -= OnOpen;
            _ws.OnMessage -= OnNewMessage;
            _ws.OnClose -= OnClose;

            _ws.Close();
            _ws = null;
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            NotifyFailure(e.Exception);

            if (AutoLogErrors)
            {
                Debug.LogException(e.Exception);
            }
        }

        private void OnOpen(object sender, EventArgs e)
        {
            NotifyConnect();

            if (AutoLogErrors)
            {
                Debug.Log("Open");
            }


            _nativePeer?.CreateOffer();
        }

        public override void OnPeerInitialized(PeerConnection peer)
        {
            base.OnPeerInitialized(peer);

            _nativePeer?.CreateOffer();
        }

        private async void OnNewMessage(object sender, MessageEventArgs e)
        {
            var msg = e.Data;
            Console.WriteLine($"web socket recv: {msg.Length} bytes");

            var header = JsonUtility.FromJson<MessageHeader>(msg);
            if (header.type == "ice")
            {
                Console.WriteLine($"Adding remote ICE candidate {msg}.");

                while (!_nativePeer.Initialized)
                {
                    // This delay is needed due to an initialise bug in the Microsoft.MixedReality.WebRTC
                    // nuget packages up to version 0.2.3. On master awaiting pc.InitializeAsync does end 
                    // up with the pc object being ready.
                    Console.WriteLine("Sleeping for 1s while peer connection is initialising...");
                    await Task.Delay(1000);
                }

                var ice = JsonUtility.FromJson<IceCandidate>(msg);
                _nativePeer.AddIceCandidate(ice.sdpMid, ice.sdpMLineindex, ice.candidate);
                return;
            }
            else if (header.type == "sdp")
            {
                Console.WriteLine("Received remote peer SDP offer.");
                
                var answer = JsonUtility.FromJson<Answer>(msg);
                if (answer.answer != null)
                {
                    await _nativePeer.SetRemoteDescriptionAsync("answer", answer.answer);
                    return;
                }

                var offer = JsonUtility.FromJson<Offer>(msg);
                if (offer.offer != null)
                {
                    await _nativePeer.SetRemoteDescriptionAsync("offer", offer.offer);

                    // if we get an offer, we immediately send an answer
                    _nativePeer.CreateAnswer();
                    return;
                }

            }

            Debug.Log("Unknown message: " + msg);
        }

        private void OnClose(object sender, CloseEventArgs e)
        {
            NotifyDisconnect();
            if (AutoLogErrors)
            {
                Debug.Log("Close");
            }

        }

        /// <summary>
        /// Callback fired when an ICE candidate message has been generated and is ready to
        /// be sent to the remote peer by the signaling object.
        /// </summary>
        /// <param name="candidate"></param>
        /// <param name="sdpMlineIndex"></param>
        /// <param name="sdpMid"></param>
        public override void OnIceCandiateReadyToSend(string candidate, int sdpMlineIndex, string sdpMid)
        {
            try
            {
                Console.WriteLine($"Sending ice candidate: {candidate}");
                _ws.Send(JsonUtility.ToJson(new IceCandidate
                {
                    type = "ice",
                    candidate = candidate,
                    sdpMLineindex = sdpMlineIndex,
                    sdpMid = sdpMid
                }));
            }
            catch (Exception e)
            {
                if (AutoLogErrors)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>
        /// Callback fired when a local SDP offer has been generated and is ready to
        /// be sent to the remote peer by the signaling object.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="sdp"></param>
        public override void OnSdpOfferReadyToSend(string offer)
        {
            try
            {
                Console.WriteLine($"Sending sdp offer: {offer}");
                _ws.Send(JsonUtility.ToJson(new Offer
                {
                    type = "sdp",
                    offer = offer
                }));
            }
            catch (Exception e)
            {
                if (AutoLogErrors)
                {
                    Debug.LogException(e);
                }
            }            
        }

        /// <summary>
        /// Callback fired when a local SDP answer has been generated and is ready to
        /// be sent to the remote peer by the signaling object.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="sdp"></param>
        public override void OnSdpAnswerReadyToSend(string answer)
        {
            try
            {
                Console.WriteLine($"Sending sdp answer: {answer}");
                _ws.Send(JsonUtility.ToJson(new Answer
                {
                    type = "sdp",
                    answer = answer
                }));
            }
            catch(Exception e)
            {
                if (AutoLogErrors)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
